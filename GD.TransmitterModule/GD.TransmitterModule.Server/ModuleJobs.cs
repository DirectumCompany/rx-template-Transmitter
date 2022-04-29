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
    /// Исходящие. Отправка сообщений по e-mail.
    /// </summary>
    public virtual void SendOutgoingMail()
    {
      var method = Sungero.Docflow.MailDeliveryMethods.GetAll(m => m.Name == Sungero.Docflow.MailDeliveryMethods.Resources.EmailMethod).FirstOrDefault();
      
      if (method == null)
      {
        throw AppliedCodeException.Create("Не найден способ доставки по e-mail.");
      }
      
      var attachmentPaths = new Dictionary<string, string>();
      
      foreach (var item in MailRegisters.GetAll(x => x.Status == GD.TransmitterModule.MailRegister.Status.ToProcess &&
                                                (x.MailType == null || x.MailType == GD.TransmitterModule.MailRegister.MailType.OutgoingLetter)))
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
        
        Functions.Module.SendDocumentAddresseesEMail(item, pathToArchive);
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
        Functions.Module.SendInternalMail(item);
      }
    }

  }
}