using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;
using System.IO;

namespace GD.TransmitterModule.Server
{
  public class ModuleJobs
  {

    /// <summary>
    /// Обновить состояние отправки корреспондента в документе.
    /// </summary>
    public virtual void UpdateDocumentsStateInternalMail()
    {
      Logger.DebugFormat("UpdateStateInSentDocuments. Start");
      var items = InternalMailRegisters.GetAll(r => r.Status == GD.TransmitterModule.InternalMailRegister.Status.Complete &&
                                               r.SyncStateInDocument == GD.TransmitterModule.InternalMailRegister.SyncStateInDocument.ToProcess &&
                                               r.LeadingDocument != null &&
                                               r.Correspondent != null);
      foreach (var item in items)
      {
        var leadingDocument = item.LeadingDocument;
        
        if (!Locks.TryLock(item))
          continue;
        
        if (!Locks.TryLock(leadingDocument))
        {
          Locks.Unlock(item);
          continue;
        }
        
        try
        {
          Logger.DebugFormat("UpdateStateInSentDocuments. Processing document {0}", item.LeadingDocument.Id);
          
          if (GD.GovernmentSolution.OutgoingLetters.Is(item.LeadingDocument))
            Functions.Module.UpdateDocumentsStateInternalMail(GD.GovernmentSolution.OutgoingLetters.As(item.LeadingDocument),
                                                              item.Correspondent,
                                                              item.CounterpartyState,
                                                              item.CounterpartyStatusInfo,
                                                              item.IsRedirect == true);
          
          if (GD.CitizenRequests.OutgoingRequestLetters.Is(item.LeadingDocument))
            Functions.Module.UpdateDocumentsStateInternalMail(GD.CitizenRequests.OutgoingRequestLetters.As(item.LeadingDocument),
                                                              item.Correspondent,
                                                              item.CounterpartyState,
                                                              item.CounterpartyStatusInfo,
                                                              item.IsRedirect == true);
          
            
          item.SyncStateInDocument = GD.TransmitterModule.InternalMailRegister.SyncStateInDocument.Complete;
          item.IsRedirect = false;
          item.Save();
        }
        catch (Exception ex)
        {
          Logger.ErrorFormat("UpdateStateInSentDocuments. An error occured while updating state in document with id = {0}", ex, leadingDocument.Id);
        }
        finally
        {
          Locks.Unlock(leadingDocument);
          Locks.Unlock(item);
        }
      }
      
      Logger.DebugFormat("UpdateStateInSentDocuments. Finish");
    }

    /// <summary>
    /// Исходящие. Отправка сообщений по e-mail.
    /// </summary>
    public virtual void SendOutgoingEMail()
    {
      var method = Sungero.Docflow.MailDeliveryMethods.GetAll(m => m.Name == Sungero.Docflow.MailDeliveryMethods.Resources.EmailMethod).FirstOrDefault();
      var settings = Functions.Module.GetTransmitterSettings();
      
      if (method == null)
      {
        throw AppliedCodeException.Create("Не найден способ доставки по e-mail.");
      }
      
      if (settings == null)
        throw AppliedCodeException.Create("Не выполнена инициализация модулей.");
      
      var records = MailRegisters.GetAll(x => x.Status == GD.TransmitterModule.MailRegister.Status.ToProcess &&
                                         (x.MailType == null || x.MailType == GD.TransmitterModule.MailRegister.MailType.OutgoingLetter));
      var maxProcessingRecordsPerRun = settings.MaxProcessingRecordsPerRun;
      records = maxProcessingRecordsPerRun == null ? records : records.Take(maxProcessingRecordsPerRun.Value);
      var attachmentPaths = new Dictionary<string, string>();
      
      foreach (var item in records)
      {
        if (Locks.GetLockInfo(item).IsLocked)
          continue;
        
        if (Locks.GetLockInfo(item.LeadingDocument).IsLocked)
          continue;
        
        var pathToArchive = string.Empty;

        if (attachmentPaths.ContainsKey(item.DocumentsSetId))
        {
          pathToArchive = attachmentPaths[item.DocumentsSetId];
        }
        else
        {
          pathToArchive = Functions.Module.GenerateArchiveWithAttachments(item);
          
          // Если вернулась пустая строка, значит при генерации архива возникли ошибки.
          if (string.IsNullOrEmpty(pathToArchive))
            continue;
          
          attachmentPaths.Add(item.DocumentsSetId, pathToArchive);
        }
        
        Functions.Module.SendDocumentAddresseesEMail(item, pathToArchive, settings.MaxRetrySendEmailCount);
      }
      
      foreach (var path in attachmentPaths)
      {
        // Удалить папку выгрузки.
        var directoryPath = Path.GetDirectoryName(path.Value);
        try
        {
          if (Directory.Exists(directoryPath))
            Directory.Delete(directoryPath, true);
        }
        catch (Exception ex)
        {
          //Logger.ErrorFormat("Отправка почтовых сообщений. Ошибка при удалении папки выгрузки. {0}", ex.Message);
          Logger.Error("Отправка почтовых сообщений. Ошибка при удалении папки выгрузки.", ex);
        }
      }
    }

    /// <summary>
    /// Исходящие. Отправка сообщений RX-RX.
    /// </summary>
    public virtual void SendOutgoingInternalMail()
    {
      foreach (var item in InternalMailRegisters.GetAll(x => x.Status == GD.TransmitterModule.InternalMailRegister.Status.ToProcess))
      {
        if (Locks.GetLockInfo(item).IsLocked)
          continue;
        
        if (item.IsRequestTransfer == true)
        {
          Functions.Module.SendInternalTransfer(item);
        }
        else
          Functions.Module.SendInternalMail(item);
      }
    }

  }
}