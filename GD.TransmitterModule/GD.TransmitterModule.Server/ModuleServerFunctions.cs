﻿using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;
using Sungero.Docflow;
using GD.GovernmentSolution;
using GD.CitizenRequests;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace GD.TransmitterModule.Server
{
  public class ModuleFunctions
  {
    /// <summary>
    /// Записать отправку в историю.
    /// </summary>
    /// <param name="letter">Исх., письмо.</param>
    [Remote]
    public virtual void WriteSendingDocsInHistory(IOutgoingLetter letter)
    {
      // Фиксация в истории отправки.
      var operation = new Enumeration(Constants.Module.SendAddressees);
      
      letter.History.Write(operation, operation, letter.Name);
        
      foreach (var doc in letter.DocsToSendGD)
      {
        letter.History.Write(operation, operation, doc.Document.Name);
      }
    }
    
    /// <summary>
    /// Записать отправку в историю.
    /// </summary>
    /// <param name="letter">Исх., письмо по обращению.</param>
    [Remote]
    public virtual void WriteSendingDocsInHistory(IOutgoingRequestLetter letter)
    {
      // Фиксация в истории отправки.
      var operation = new Enumeration(Constants.Module.SendAddressees);
      
      letter.History.Write(operation, operation, letter.Name);
        
      foreach (var doc in letter.DocsToSendGD)
      {
        letter.History.Write(operation, operation, doc.Document.Name);
      }
    }
    
    /// <summary>
    /// Отправить исх., документ перенаправлением.
    /// </summary>
    /// <param name="transferRecord">Элемент очереди отправки.</param>
    [Public]
    public virtual void SendInternalTransfer(IInternalMailRegister transferRecord)
    {
      var outgoingRequestLetter = GD.CitizenRequests.OutgoingRequestLetters.As(transferRecord.LeadingDocument);
      
      try
      {
        if (!Locks.TryLock(outgoingRequestLetter))
          return;
        
        var correspondent = transferRecord.Correspondent;
        
        if (outgoingRequestLetter == null)
          throw AppliedCodeException.Create(Resources.DocumentNotFound);
        
        var task = CitizenRequests.PublicFunctions.Module.Remote.StartInternalTransfer(outgoingRequestLetter, transferRecord.Request, correspondent);
        var deliveryMethodSid = GD.CitizenRequests.PublicFunctions.Module.Remote.GetDirectumRXDeliveryMethodSid();
        var addresses = outgoingRequestLetter.Addressees.Cast<IOutgoingRequestLetterAddressees>().Where(a => Equals(a.Correspondent, correspondent) &&
                                                                                                        a.DeliveryMethod != null &&
                                                                                                        a.DeliveryMethod.Sid == deliveryMethodSid &&
                                                                                                        a.DocumentState == Resources.AwaitingDispatch).FirstOrDefault();
        CitizenRequests.PublicFunctions.Module.Remote.SetRequestLetterTransferStatus(outgoingRequestLetter,
                                                                                     addresses.Correspondent,
                                                                                     CitizenRequests.PublicConstants.Module.InternalTransferDeliveryStatus.Sent,
                                                                                     CitizenRequests.PublicConstants.Module.InternalTransferDeliveryStatus.Sent);
        
        // Заполнить дату отправки т.к. в ф-ции SetRequestLetterTransferStatus заполняется только информация в ТЧ по статусу контрагента и информация по статусу.
        var addressee = outgoingRequestLetter.Addressees.Cast<IOutgoingRequestLetterAddressees>().Where(a => Equals(a.Correspondent, addresses.Correspondent)).FirstOrDefault();
        addressee.ForwardDate = Calendar.Today;
        
        outgoingRequestLetter.Save();
        transferRecord.Status = GD.TransmitterModule.InternalMailRegister.Status.Complete;
        transferRecord.TaskId = task.Id;
        transferRecord.Save();
      }
      catch (Exception ex)
      {
        Logger.ErrorFormat("TransmitterModule. ФП Исходящие. Отправка сообщений RX-RX. Произошла ошибка при отправке исх. документа перенаправлением в записи id = {0} \"Реестра отправки сообщений в рамках системы\": {1}.",
                           transferRecord.Id, ex);
        transferRecord.ErrorInfo = ex.Message.Substring(0, ex.Message.Length < 1000 ? ex.Message.Length : 1000);
        transferRecord.Status = GD.TransmitterModule.InternalMailRegister.Status.Error;
        transferRecord.Save();
      }
      finally
      {
        if (Locks.GetLockInfo(outgoingRequestLetter).IsLockedByMe)
          Locks.Unlock(outgoingRequestLetter);
      }
    }
    
    /// <summary>
    /// Проверить размер архива со всеми вложениями письма на допустимое значение.
    /// </summary>
    /// <param name="letter">Исходящее письмо.</param>
    /// <param name="maxAttachmentFileSize">Максимальный разрешенный размер вложения (Мб).</param>
    /// <returns>true, если размер архива вложений не больше, чем значение из настроек модуля, иначе - false.</returns>
    [Public, Remote(IsPure=true)]
    public virtual bool CheckPackageSize(IOutgoingDocumentBase letter, double maxAttachmentFileSize)
    {
      var result = false;
      try
      {
        var letterName = letter.Name.Length >= 80 ? letter.Name.Substring(0, 80) : letter.Name;
        var pathForDoc = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var attachmentsPath = Directory.CreateDirectory(Path.Combine(pathForDoc, Guid.NewGuid().ToString()));
        var maxDocNameLength = 120;
        
        var docsToSend = new List<Sungero.Content.IElectronicDocument>();
        if (OutgoingLetters.Is(letter))
        {
          foreach (var relatedDoc in OutgoingLetters.As(letter).DocsToSendGD)
          {
            if (relatedDoc.Document != null && relatedDoc.Document.HasVersions)
            {
              docsToSend.Add(relatedDoc.Document);
            }
          }
        }
        
        if (docsToSend.Any())
        {
          foreach (var item in docsToSend)
          {
            using (var body = item.LastVersion.PublicBody.Size == 0 ? item.LastVersion.Body.Read() : item.LastVersion.PublicBody.Read())
            {
              var itemName = item.Name.Length >= 80 ? item.Name.Substring(0, 80) : item.Name;
              using (var fileStreamPublic = File.Create(GenerateFilePath(attachmentsPath.FullName, itemName, item.LastVersion.AssociatedApplication.Extension, maxDocNameLength)))
              {
                body.Seek(0, SeekOrigin.Begin);
                body.CopyTo(fileStreamPublic);
              }
            }
          }
        }
        
        using (var ms = letter.LastVersion.PublicBody.Size == 0 ? letter.LastVersion.Body.Read() : letter.LastVersion.PublicBody.Read())
        {
          using (var fileStreamPublic = File.Create(GenerateFilePath(attachmentsPath.FullName, letterName, letter.LastVersion.AssociatedApplication.Extension, maxDocNameLength)))
          {
            ms.Seek(0, SeekOrigin.Begin);
            ms.CopyTo(fileStreamPublic);
          }
        }
        
        // Выгрузить оригинал и подпись, заархивировать и добавить вложение в письмо.
        var signatory = letter.OurSignatory;
        var signatures = Signatures.Get(letter.LastVersion).Where(s => s.SignatureType == SignatureType.Approval);
        if (signatory != null && signatures.Where(s => s.SignatoryFullName == signatory.Name).Any())
          signatures = signatures.Where(s => s.SignatoryFullName == signatory.Name);
        
        if (signatures.Any())
        {
          var signatureText = signatures.FirstOrDefault().GetDataSignature();
          using (var fileStreamSignature = File.Create(GenerateFilePath(attachmentsPath.FullName, letter.Name, "sig", maxDocNameLength)))
            fileStreamSignature.Write(signatureText, 0, signatureText.Length);
          
          if (letter.LastVersion.AssociatedApplication.Extension == "pdf")
          {
            using (var streamOriginal = letter.LastVersion.Body.Read())
            {
              using (var fileStreamOriginal = File.Create(GenerateFilePath(attachmentsPath.FullName, string.Format("{0}_original", letterName), letter.LastVersion.BodyAssociatedApplication.Extension, maxDocNameLength)))
              {
                streamOriginal.Seek(0, SeekOrigin.Begin);
                streamOriginal.CopyTo(fileStreamOriginal);
              }
            }
          }
        }
        var zipName = Path.Combine(pathForDoc, "Документы_к_отправке.zip");
        if (File.Exists(zipName))
        {
          File.Delete(zipName);
        }
        Logger.DebugFormat("!!! CheckPackageSize : 5 - 1 Создание архива ");
        Functions.Module.CreateFromDirectoryPublic(attachmentsPath.FullName, zipName);
        
        FileInfo file = new FileInfo(zipName);
        result = maxAttachmentFileSize >= (file.Length / (double)1048576);
        
        // Удалить папку выгрузки.
        try
        {
          if (Directory.Exists(pathForDoc))
            Directory.Delete(pathForDoc, true);
        }
        catch (Exception ex)
        {
          Logger.ErrorFormat("CheckPackageSize. Ошибка при удалении папки выгрузки. {0}", ex);
        }
      }
      catch (Exception ex)
      {
        Logger.ErrorFormat("CheckPackageSize. При проверке возникла ошибка {0}", ex);
      }
      return result;
    }

    public virtual void SendNoticeToResponsible(IMailRegister mailRegister)
    {
      var letter = mailRegister != null ? Sungero.Docflow.OfficialDocuments.As(mailRegister.LeadingDocument) : Sungero.Docflow.OfficialDocuments.Null;
      var attachments = new List<Sungero.Domain.Shared.IEntity>();
      if (mailRegister != null)
      {
        attachments.Add(mailRegister);
      }
      if (letter != null)
      {
        attachments.Add(letter);
      }
      
      var addressee = GetResponsibleForEmailSending();
      var task = Sungero.Workflow.SimpleTasks.CreateWithNotices(GD.TransmitterModule.Resources.SendError, new List<IRecipient> {addressee}, attachments.ToArray());
      task.ActiveText = mailRegister.ErrorInfo;
      task.Start();
    }
    
    public virtual void SendNoticeToResponsible(IInternalMailRegister mailRegister)
    {
      var letter = mailRegister != null ? Sungero.Docflow.OfficialDocuments.As(mailRegister.LeadingDocument) : Sungero.Docflow.OfficialDocuments.Null;
      var attachments = new List<Sungero.Domain.Shared.IEntity>();
      if (mailRegister != null)
      {
        attachments.Add(mailRegister);
      }
      
      var addressee = Roles.Administrators;
      var task = Sungero.Workflow.SimpleTasks.CreateWithNotices(GD.TransmitterModule.Resources.SendingDocumentsErrors, new List<IRecipient> {addressee}, attachments.ToArray());
      task.ActiveText = mailRegister.ErrorInfo;
      task.Start();
    }
    
    public virtual IRecipient GetResponsibleForEmailSending()
    {
      return Roles.GetAll(r => r.Sid == Guid.Empty).FirstOrDefault() ?? Roles.Administrators;
    }
    
    /// <summary>
    /// Получить настройки модуля отправки писем.
    /// </summary>
    [Public, Remote]
    public virtual ITransmitterSetting GetTransmitterSettings()
    {
      return TransmitterSettings.GetAll().FirstOrDefault();
    }
    
    /// <summary>
    /// Создать настройки модуля отправки писем.
    /// </summary>
    public virtual void CreateTransmitterSettings()
    {
      var settings = TransmitterSettings.Create();
      settings.Name = TransmitterSettings.Info.LocalizedName;
      settings.MaxAttachmentFileSize = 15;
      settings.Save();
    }
    
    /// <summary>
    /// Создать копию приложения.
    /// </summary>
    /// <param name="addendum">Копируемое приложение.</param>
    /// <returns>Копия приложения.</returns>
    [Public, Remote]
    public static Sungero.Docflow.IAddendum CopyAddendum(Sungero.Docflow.IAddendum addendum)
    {
      var copyAddendum = Sungero.Docflow.Addendums.Create();
      if (string.IsNullOrEmpty(addendum.Subject) || string.IsNullOrWhiteSpace(addendum.Subject))
        copyAddendum.Subject = addendum.Name;
      else
        copyAddendum.Subject = addendum.Subject;
      // Создать версию приложения.
      copyAddendum.CreateVersion();
      var copyAddendumVersion = copyAddendum.LastVersion;
      var addendumDocVersion = addendum.LastVersion;
      var docVersionStream = new System.IO.MemoryStream();
      var addendumDocBody = addendum.HasPublicBody == true ? addendumDocVersion.PublicBody : addendumDocVersion.Body;
      using (var sourceStream = addendumDocBody.Read())
        sourceStream.CopyTo(docVersionStream);
      copyAddendumVersion.Body.Write(docVersionStream);
      copyAddendumVersion.AssociatedApplication = addendumDocVersion.AssociatedApplication;
      
      return copyAddendum;
    }
    
    /// <summary>
    /// Обновить статус у контрагента в исходящем письме.
    /// <param name="doc">Документ к отправке.</param>
    /// <param name="counterparty">Корреспондент.</param>
    /// <param name="newState">Новый статус.</param>
    /// <param name="comment">Комментарий.</param>
    /// </summary>
    public static void ChangeReasonDocState(Sungero.Docflow.IOfficialDocument doc, Sungero.Parties.ICounterparty counterparty, string newState, string comment)
    {
      if (Sungero.Docflow.OutgoingDocumentBases.Is(doc))
      {
        var directumRXDeliveryMethodSid = CitizenRequests.PublicFunctions.Module.Remote.GetDirectumRXDeliveryMethodSid();
        var addressee = Sungero.Docflow.OutgoingDocumentBases.As(doc).Addressees
          .Where(a => a.DeliveryMethod?.Sid == directumRXDeliveryMethodSid &&
                 Equals(a.Correspondent, counterparty)).Last();
        if (addressee != null)
        {
          if (OutgoingLetters.Is(doc))
          {
            ((IOutgoingLetterAddressees)addressee).DocumentState = newState;
            ((IOutgoingLetterAddressees)addressee).StateInfo = comment != null ? comment : newState;
          }
          else
          {
            ((IOutgoingRequestLetterAddressees)addressee).DocumentState = newState;
            ((IOutgoingRequestLetterAddressees)addressee).StateInfo = comment != null ? comment : newState;
          }
        }
        if (Sungero.Docflow.OutgoingDocumentBases.As(doc).IsManyAddressees == false)
        {
          if (OutgoingLetters.Is(doc))
          {
            OutgoingLetters.As(doc).DocumentState = newState;
            OutgoingLetters.As(doc).StateInfo = comment != null ? comment : newState;
          }
          else
          {
            OutgoingRequestLetters.As(doc).DocumentState = newState;
            OutgoingRequestLetters.As(doc).StateInfo = comment != null ? comment : newState;
          }
        }
      }
      doc.Save();
    }
    
    /// <summary>
    /// Сгенерировать архив с вложениями письма.
    /// </summary>
    /// <param name="mailRegister">Запись реестра отправки</param>
    /// <returns>Путь до сформированного файла архива.</returns>
    public virtual string GenerateArchiveWithAttachments(IMailRegister mailRegister)
    {
      var letter = OutgoingDocumentBases.As(mailRegister.LeadingDocument);
      
      try
      {
        if (!Locks.TryLock(letter))
          return string.Empty;
        
        if(!Locks.TryLock(mailRegister))
        {
          if (Locks.GetLockInfo(letter).IsLockedByMe)
            Locks.Unlock(letter);
          
          return string.Empty;
        }
        
        if (!letter.HasVersions)
        {
          throw AppliedCodeException.Create("У документа нет ни одной версии.");
        }
        
        //var isSendCopy = mailRegister.IsCopySend == true;
        var pathForDoc = Path.Combine(Path.GetTempPath(), mailRegister.Id.ToString());
        var attachmentsPath = Directory.CreateDirectory(Path.Combine(pathForDoc, Calendar.Now.ToString().Replace(" ", "").Replace(".", "").Replace(":", "")));
        var maxDocNameLength = 240 - attachmentsPath.FullName.Length;
        
        var letterName = letter.Name.Length >= 80 ? letter.Name.Substring(0, 80) : letter.Name;
        Logger.DebugFormat("Debug SendDocumentAddresseesEMail - 1 = " + letter.Name);
        
        
        Logger.DebugFormat("Debug SendDocumentAddresseesEMail - 2 Add applications");
        
        var docsToSend = new List<Sungero.Content.IElectronicDocument>();
        foreach (var relatedDoc in mailRegister.RelatedDocuments)
        {
          if (relatedDoc.Document != null && relatedDoc.Document.HasVersions)
          {
            docsToSend.Add(relatedDoc.Document);
          }
        }

        if (docsToSend.Any())
        {
          foreach (var item in docsToSend)
          {
            using (var body = item.LastVersion.PublicBody.Size == 0 ? item.LastVersion.Body.Read() : item.LastVersion.PublicBody.Read())
            {
              var itemName = item.Name.Length >= 80 ? item.Name.Substring(0, 80) : item.Name;
              using (var fileStreamPublic = File.Create(GenerateFilePath(attachmentsPath.FullName, itemName, item.LastVersion.AssociatedApplication.Extension, maxDocNameLength)))
              {
                body.Seek(0, SeekOrigin.Begin);
                body.CopyTo(fileStreamPublic);
              }
            }
          }
        }
        Logger.DebugFormat("Debug SendDocumentAddresseesEMail - 3");
        
        var isNeedToConvert = false;
        var isPDF = false;
        
        var signatory = letter.OurSignatory;
        var signatures = Signatures.Get(letter.LastVersion).Where(s => s.SignatureType == SignatureType.Approval);
        if (signatory != null && signatures.Where(s => s.SignatoryFullName == signatory.Name).Any())
          signatures = signatures.Where(s => s.SignatoryFullName == signatory.Name);
        
        if (letter.LastVersion.BodyAssociatedApplication.Extension == "pdf")
        {
          using (var ms = letter.LastVersion.PublicBody.Size == 0 ? letter.LastVersion.Body.Read() : letter.LastVersion.PublicBody.Read())
          {
            using (var fileStreamPublic = File.Create(GenerateFilePath(attachmentsPath.FullName, letterName, letter.LastVersion.AssociatedApplication.Extension, maxDocNameLength)))
            {
              ms.Seek(0, SeekOrigin.Begin);
              ms.CopyTo(fileStreamPublic);
            }
          }
        }
        else
        {
          if (letter.LastVersion.AssociatedApplication.Extension != "pdf" && signatures.Any())
          {
            // Добавление возможности автоматического преобразования отправляемого документа в pdf и установки отметки об ЭП.
            //isNeedToConvert = true;
            /*if (letter.LastVersion.AssociatedApplication.Extension == "doc" || letter.LastVersion.AssociatedApplication.Extension == "docx" ||
                    letter.LastVersion.AssociatedApplication.Extension == "xls" || letter.LastVersion.AssociatedApplication.Extension == "xlsx" ||
                    letter.LastVersion.AssociatedApplication.Extension == "odt" || letter.LastVersion.AssociatedApplication.Extension == "ods")
                {
                  try
                  {
                    Functions.Module.ConvertToPdfAndAddSignatureMark(Sungero.Docflow.OfficialDocuments.As(letter), letter.LastVersion.Id);
                    letter.Save();
                    
                    if (letter.LastVersion.AssociatedApplication.Extension == "pdf")
                      isPDF = true;
                  }
                  catch (Exception ex)
                  {
                    Logger.Error(ex.Message);
                    
                    if (mailRegister.Iteration < 4)
                      return;
                    else
                    {
                      mailRegister.ErrorInfo = ex.Message;
                      mailRegister.Status = GD.TransmitterModule.MailRegister.Status.Error;
                      mailRegister.Save();
                      var config = DirRX.Support.PublicFunctions.Configuration.Remote.DefaultConfiguration();
                      var att = new List<Sungero.Domain.Shared.IEntity>();
                      att.Add(mailRegister);
                      att.Add(letter);
                      if (config != null && config.NotifAddressees.Any())
                        foreach (var addresseeItem in config.NotifAddressees.Select(x => x.Addressee))
                          DirRX.SiberlinkConnect.PublicFunctions.Module.Remote.SendNotification(false, addresseeItem, "Отправка почтовых сообщений. Ошибка при отправке сообщения.", "Не удалось преобразовать отправляемый документ в PDF.", att);
                      return;
                    }
                  }
                }*/
          }
          
          mailRegister.Extension = letter.LastVersion.AssociatedApplication.Extension;
          mailRegister.Save();
          
          using (var ms = letter.LastVersion.PublicBody.Size == 0 ?
                 letter.LastVersion.Body.Read() :
                 letter.LastVersion.PublicBody.Read())
          {
            using (var fileStreamPublic = File.Create(GenerateFilePath(attachmentsPath.FullName, letterName, letter.LastVersion.AssociatedApplication.Extension, maxDocNameLength)))
            {
              ms.Seek(0, SeekOrigin.Begin);
              ms.CopyTo(fileStreamPublic);
            }
          }
          
          if (letter.LastVersion.AssociatedApplication.Extension == "pdf")
          {
            if (isNeedToConvert)
            {
              var lastVersionBody = new System.IO.MemoryStream();
              var lastVersionExt = letter.LastVersion.BodyAssociatedApplication.Extension;
              using (var sourceStream = letter.LastVersion.Body.Read())
                sourceStream.CopyTo(lastVersionBody);
              letter.LastVersion.PublicBody.Write(lastVersionBody);
              letter.LastVersion.AssociatedApplication = Sungero.Content.AssociatedApplications.GetByExtension(lastVersionExt);
              letter.Save();
            }
          }
        }
        
        // Выгрузить оригинал и подпись, заархивировать и добавить вложение в письмо.
        
        if (signatures.Any())
        {
          var signatureText = signatures.FirstOrDefault().GetDataSignature();
          var fileStreamSignature = File.Create(GenerateFilePath(attachmentsPath.FullName, letterName, "sig", maxDocNameLength));
          fileStreamSignature.Write(signatureText, 0, signatureText.Length);
          fileStreamSignature.Close();
          
          if (isPDF || letter.LastVersion.AssociatedApplication.Extension == "pdf")
          {
            using (var streamOriginal = letter.LastVersion.Body.Read())
            {
              using (var fileStreamOriginal = File.Create(GenerateFilePath(attachmentsPath.FullName, string.Format("{0}_original", letterName), letter.LastVersion.BodyAssociatedApplication.Extension, maxDocNameLength)))
              {
                streamOriginal.Seek(0, SeekOrigin.Begin);
                streamOriginal.CopyTo(fileStreamOriginal);
              }
            }
          }
        }
        var zipName = Path.Combine(pathForDoc, "Документы_к_отправке.zip");
        if (File.Exists(zipName))
        {
          File.Delete(zipName);
        }
        Logger.DebugFormat("Debug SendDocumentAddresseesEMail - 4-1 Создание архива ");
        PublicFunctions.Module.Remote.CreateFromDirectoryPublic(attachmentsPath.FullName, zipName);
        Logger.DebugFormat("Debug SendDocumentAddresseesEMail - 4-2 добавление архива во вложение ");
        
        return zipName;
      }
      catch (Exception ex)
      {
        Logger.Error("Отправка почтовых сообщений. Ошибка при создании архива.", ex);
        mailRegister.Iteration++;
        mailRegister.ErrorInfo = ex.Message.Substring(0, ex.Message.Length < 1000 ? ex.Message.Length : 1000);
        mailRegister.Status = GD.TransmitterModule.MailRegister.Status.Error;
        mailRegister.Extension = letter != null ? letter.LastVersion.AssociatedApplication.Extension : string.Empty;
        mailRegister.Save();
        return string.Empty;
      }
      finally
      {
        if (Locks.GetLockInfo(mailRegister).IsLockedByMe)
          Locks.Unlock(mailRegister);
        
        if (Locks.GetLockInfo(letter).IsLockedByMe)
          Locks.Unlock(letter);
      }
    }
    
    /// <summary>
    /// Создать архив.
    /// <param name="archivePath">Путь до папки с архивом.</param>
    /// <param name="zipName">Наименование архива.</param>
    /// </summary>
    [Public, Remote]
    public static void CreateFromDirectoryPublic(string archivePath, string zipName)
    {
      ZipFile.CreateFromDirectory(archivePath, zipName);
    }
    
    /// <summary>
    /// Генерировать полный путь к файлу.
    /// <param name="folderPath">Путь до папки.</param>
    /// <param name="documentName">Наименование файла.</param>
    /// <param name="extensionName">Расширение.</param>
    /// <param name="maxDocNameLength">Максимальная длина наименования файла.</param>
    /// </summary>
    public string GenerateFilePath(string folderPath, string documentName, string extensionName, int maxDocNameLength)
    {
      var fileName = documentName.Replace(@"/", "_").Replace("\"", "_");
      var appName = string.Format(@"{0}.{1}", fileName.Length > maxDocNameLength - extensionName.Length - 1 ?
                                  fileName.Substring(0, maxDocNameLength - extensionName.Length - 1) :
                                  fileName, extensionName);
      return Path.Combine(folderPath, appName);
    }
    
    /// <summary>
    /// Получить значение свойства "Регистрация" документа.
    /// <param name="document">Идентификатор документа.</param>
    /// </summary>
    [Remote(IsPure = true), Public]
    public static Sungero.Core.Enumeration GetDocRegState(int id)
    {
      return Sungero.Docflow.OfficialDocuments.Get(id).RegistrationState.Value;
    }
    
    /// <summary>
    /// Проверить на валидность все вложения задачи.
    /// </summary>
    /// <param name="task">Задача.</param>
    [Public]
    public static bool CheckAttachmentIsValid(Sungero.Workflow.ITask task)
    {
      var emptyMainDoc =  task.Attachments.Where(a => Sungero.Docflow.Addendums.Is(a) && Sungero.Docflow.Addendums.As(a).LeadingDocument == null).Any();
      return emptyMainDoc;
    }
    
    /// <summary>
    /// Отправка исходящего документа в DirectumRX.
    /// </summary>
    /// <param name="item">Элемент очереди отправки.</param>
    [Public]
    public void SendInternalMail(IInternalMailRegister item)
    {
      var document = Sungero.Docflow.OfficialDocuments.As(item.LeadingDocument);
      
      try
      {
        if (!Locks.TryLock(document))
          return;
        
        var correspondent = item.Correspondent;
        
        if (document == null)
          throw AppliedCodeException.Create(Resources.DocumentNotFound);
        
        var procTask = IncomingDocumentProcessingTasks.Create();
        procTask.ReasonDoc = document;
        procTask.ToBusinessUnit = Sungero.Company.BusinessUnits.GetAll().Where(b => b.Status == Sungero.CoreEntities.DatabookEntry.Status.Active && Companies.Equals(b.Company, correspondent)).FirstOrDefault();
        procTask.ToCounterparty = correspondent;
        
        foreach (var relatedDoc in item.RelatedDocuments.Where(x => x.Document != null).Select(x => x.Document))
        {
          var newAddendum = procTask.Addendums.AddNew();
          newAddendum.Reason = relatedDoc;
        }
        procTask.CreatedFrom = item;
        
        procTask.Save();
        procTask.Start();
        
        if (OutgoingLetters.Is(document))
        {
          var outgoingLetter = OutgoingLetters.As(document);
          var directumRXDeliveryMethodSid = CitizenRequests.PublicFunctions.Module.Remote.GetDirectumRXDeliveryMethodSid();
          var addressee = (IOutgoingLetterAddressees)outgoingLetter.Addressees.Where(x => x.Correspondent != null &&
                                                                                     Equals(x.Correspondent, correspondent) &&
                                                                                     x.DeliveryMethod?.Sid == directumRXDeliveryMethodSid).FirstOrDefault();
          if (addressee != null)
          {
            addressee.DocumentState = Resources.DeliveryState_Sent;
            addressee.StateInfo = Resources.DeliveryState_Sent;
            addressee.ForwardDateGD = Calendar.Today;
          }
          if (outgoingLetter.IsManyAddressees == false)
          {
            outgoingLetter.DocumentState = Resources.DeliveryState_Sent;
            outgoingLetter.StateInfo = Resources.DeliveryState_Sent;
          }
          
          outgoingLetter.Save();
        }
        else if (OutgoingRequestLetters.Is(document))
        {
          var outgoingRequestLetter = OutgoingRequestLetters.As(document);
          var directumRXDeliveryMethodSid = CitizenRequests.PublicFunctions.Module.Remote.GetDirectumRXDeliveryMethodSid();
          var addressee = (IOutgoingRequestLetterAddressees)outgoingRequestLetter.Addressees.Where(x => x.Correspondent != null &&
                                                                                                   Equals(x.Correspondent, correspondent) &&
                                                                                                   x.DeliveryMethod?.Sid == directumRXDeliveryMethodSid).FirstOrDefault();
          if (addressee != null)
          {
            addressee.DocumentState = Resources.DeliveryState_Sent;
            addressee.StateInfo = Resources.DeliveryState_Sent;
          }
          
          if (outgoingRequestLetter.IsManyAddressees == false)
          {
            outgoingRequestLetter.DocumentState = Resources.DeliveryState_Sent;
            outgoingRequestLetter.StateInfo = Resources.DeliveryState_Sent;
          }
          
          outgoingRequestLetter.Save();
        }
        
        item.Status = GD.TransmitterModule.InternalMailRegister.Status.Complete;
        item.TaskId = procTask.Id;
        item.Save();
      }
      catch (Exception ex)
      {
        Logger.ErrorFormat("TransmitterModule. ФП Исходящие. Отправка сообщений RX-RX. Произошла ошибка при отправке исх. документа в DirecumRX в записи id = {0} \"Реестра отправки сообщений в рамках системы\": {1}.",
                           item.Id, ex);
        item.ErrorInfo = ex.Message.Substring(0, ex.Message.Length < 1000 ? ex.Message.Length : 1000);
        item.Status = GD.TransmitterModule.InternalMailRegister.Status.Error;
        item.Save();
      }
      finally
      {
        if (Locks.GetLockInfo(document).IsLockedByMe)
          Locks.Unlock(document);
      }
    }
    
    /// <summary>
    /// Отправка исх. писем адресатам по e-mail.
    /// </summary>
    /// <param name="mailRegister">Запись реестра отправки.</param>
    /// <param name="zipName">Путь до архива с вложениями.</param>
    public void SendDocumentAddresseesEMail(IMailRegister mailRegister, string zipName)
    {
      var letter = OutgoingDocumentBases.As(mailRegister.LeadingDocument);
      
      try
      {
        if (!Locks.TryLock(letter))
          return;
        
        mailRegister.Iteration++;
        mailRegister.Save();
        
        var mail = Mail.CreateMailMessage();
        mail.Body = Resources.MailTemplateFormat(letter.BusinessUnit != null ? letter.BusinessUnit.Name : string.Empty,
                                                 mailRegister.Sender != null ? mailRegister.Sender.Name : string.Empty,
                                                 letter.BusinessUnit != null ? letter.BusinessUnit.Email : string.Empty);
        mail.IsBodyHtml = true;
        mail.Subject = Sungero.Docflow.OfficialDocuments.Resources.SendByEmailSubjectPrefixFormat(letter.Name);
        
        FileStream attachmentStream = null;
        if (File.Exists(zipName))
        {
          attachmentStream = File.OpenRead(zipName);
          mail.AddAttachment(attachmentStream, "Документы к отправке.zip");
          Logger.DebugFormat("Debug SendDocumentAddresseesEMail - 4-3 добавлен архив во вложение ");
        }
        Logger.DebugFormat("Debug SendDocumentAddresseesEMail - 5 ");
        var isError = false;
        if (!string.IsNullOrWhiteSpace(mailRegister.Correspondent.Email))
        {
          var email = mailRegister.Correspondent.Email;;
          Logger.DebugFormat("Debug SendDocumentAddresseesEMail - 6 = " + email);
          var mailAddress = new System.Net.Mail.MailAddress(email);
          mail.To.Add(email);
          Logger.DebugFormat("Debug SendDocumentAddresseesEMail - 7 Before Send");
          try
          {
            Mail.Send(mail);
            mailRegister.Status = GD.TransmitterModule.MailRegister.Status.Complete;
            mailRegister.ErrorInfo = string.Empty;
            mailRegister.Save();
          }
          //          catch (SmtpFailedRecipientsException ex)
          //          {
          //            isError = true;
          //            var errorMessage = string.Empty;
          //            errorMessage += ex.InnerExceptions.ToList().Select(x => x.Message) + "\n";
          //            mailRegister.Status = GD.TransmitterModule.MailRegister.Status.Error;
          //            mailRegister.ErrorInfo = errorMessage;
          //            mailRegister.Save();
          //            /*var config = DirRX.Support.PublicFunctions.Configuration.Remote.DefaultConfiguration();
          //                var att = new List<Sungero.Domain.Shared.IEntity>();
          //                att.Add(mailRegister);
          //                att.Add(letter);
          //                if (config != null && config.NotifAddressees.Any())
          //                  foreach (var addresseeItem in config.NotifAddressees.Select(x => x.Addressee))
          //                    DirRX.SiberlinkConnect.PublicFunctions.Module.Remote.SendNotification(false, addresseeItem, "Отправка почтовых сообщений. Ошибка при отправке сообщения.", errorMessage, att);*/
          //          }
          catch (Exception ex)
          {
            Logger.Error("Отправка почтовых сообщений. Ошибка непосредственно при отправке.", ex);
            isError = true;
            mailRegister.Status = GD.TransmitterModule.MailRegister.Status.Error;
            mailRegister.ErrorInfo = ex.Message;
            mailRegister.Save();
            /*var config = DirRX.Support.PublicFunctions.Configuration.Remote.DefaultConfiguration();
                var att = new List<Sungero.Domain.Shared.IEntity>();
                att.Add(mailRegister);
                att.Add(letter);
                if (config != null && config.NotifAddressees.Any())
                  foreach (var addresseeItem in config.NotifAddressees.Select(x => x.Addressee))
                    DirRX.SiberlinkConnect.PublicFunctions.Module.Remote.SendNotification(false, addresseeItem, "Отправка почтовых сообщений. Ошибка при отправке сообщения.", ex.Message, att);*/
          }
          Logger.DebugFormat("Debug SendDocumentAddresseesEMail - 8 After Send");
          
          var addressee = letter.Addressees.FirstOrDefault(x => x.Id == mailRegister.AddresseeId);
          Logger.DebugFormat("Debug SendDocumentAddresseesEMail - 9  Update state and forward date to letter");
          if (addressee != null && !isError)
          {
            if (OutgoingLetters.Is(letter))
            {
              (addressee as IOutgoingLetterAddressees).DocumentState = Resources.DeliveryState_Sent;
              (addressee as IOutgoingLetterAddressees).StateInfo = Resources.DeliveryState_Sent;
              (addressee as IOutgoingLetterAddressees).ForwardDateGD = Calendar.Today;
            }
            else if (OutgoingRequestLetters.Is(letter))
            {
              (addressee as IOutgoingRequestLetterAddressees).DocumentState = Resources.DeliveryState_Sent;
              (addressee as IOutgoingRequestLetterAddressees).StateInfo = Resources.DeliveryState_Sent;
              (addressee as IOutgoingRequestLetterAddressees).ForwardDate = Calendar.Today;
            }
          }
          Logger.DebugFormat("Debug SendDocumentAddresseesEMail - 10  Finally");
        }
        attachmentStream.Dispose();
        letter.Save();
      }
      catch (Exception ex)
      {
        //Logger.Error(ex.Message);
        Logger.Error("Отправка почтовых сообщений. Ошибка при обработке.", ex);
        mailRegister.ErrorInfo = ex.Message.Substring(0, ex.Message.Length < 1000 ? ex.Message.Length : 1000);
        mailRegister.Status = GD.TransmitterModule.MailRegister.Status.Error;
        mailRegister.Extension = letter != null ? letter.LastVersion.AssociatedApplication.Extension : string.Empty;
        mailRegister.Save();
        /*var config = DirRX.Support.PublicFunctions.Configuration.Remote.DefaultConfiguration();
        var att = new List<Sungero.Domain.Shared.IEntity>();
        att.Add(mailRegister);
        att.Add(letter);
        if (config != null && config.NotifAddressees.Any())
          foreach (var addresseeItem in config.NotifAddressees.Select(x => x.Addressee))
            DirRX.SiberlinkConnect.PublicFunctions.Module.Remote.SendNotification(false, addresseeItem, "Отправка почтовых сообщений. Ошибка при отправке сообщения.", ex.Message, att);*/
      }
      finally
      {
        if (Locks.GetLockInfo(letter).IsLockedByMe)
          Locks.Unlock(letter);
      }
    }
    
    /// <summary>
    /// Получить ответственного регистратора для организации и вида документа.
    /// <param name="businessUnit">Наша организация.</param>
    /// <param name="documentKind">Вид документа.</param>
    /// <returns></returns>
    /// </summary>
    [Public, Remote]
    public Sungero.Company.IEmployee GetRegistrarForBusinessUnit(Sungero.Company.IBusinessUnit businessUnit, Sungero.Docflow.IDocumentKind documentKind)
    {
      var documentSettings = Sungero.Docflow.RegistrationSettings.GetAll().Where(s => s.SettingType == Sungero.Docflow.RegistrationSetting.SettingType.Registration &&
                                                                                 s.Status == Sungero.Docflow.RegistrationSetting.Status.Active &&
                                                                                 s.DocumentKinds.Any(t => Equals(t.DocumentKind, documentKind)) &&
                                                                                 s.BusinessUnits.Any(t => Equals(t.BusinessUnit, businessUnit)) &&
                                                                                 s.DocumentRegister.RegistrationGroup != null).FirstOrDefault();
      if (documentSettings == null)
        return null;
      
      return documentSettings.DocumentRegister.RegistrationGroup.ResponsibleEmployee;
    }
    
    /// <summary>
    /// Отправка исходящего документа по МЭДО.
    /// </summary>
    /// <param name="document">Документ к отправке.</param>
    /// <param name="company">Адресат.</param>
    /// <returns>Ошибки при отправке.</returns>
    [Public]
    public void MEDOSendToCounterparty(Sungero.Docflow.IOutgoingDocumentBase document, IQueryable<Sungero.Content.IElectronicDocument> relatedDocs, ICompany company, List<string> errors)
    {
      var documentPages = new List<string>();
      
      foreach (var doc in relatedDocs)
      {
        documentPages.Add(string.Format("{0}/{1}", doc.Id, 0));
      }
      
      try
      {
        var package = MEDO.PublicFunctions.Module.Remote.CreatePackage(document, company, documentPages, !OutgoingLetters.Is(document));
        if (OutgoingLetters.Is(document))
        {
          MEDO.PublicFunctions.Module.Remote.SetDocumentMedoStatus(Resources.DeliveryState_Sent, Resources.DeliveryState_Sent, OutgoingLetters.As(document), package);
          
          // Заполнить дату отправки т.к. в ф-ции SetRequestMedoStatus заполняется только информация в ТЧ по статусу контрагента и информация по статусу.
          var addressee = document.Addressees.Cast<IOutgoingLetterAddressees>().Where(a => Equals(a.Correspondent, company)).FirstOrDefault();
          addressee.ForwardDateGD = Calendar.Today;
        }
        else
        {
          MEDO.PublicFunctions.Module.Remote.SetRequestMedoStatus(Resources.DeliveryState_Sent, Resources.DeliveryState_Sent, OutgoingRequestLetters.As(document), package);
          
          // Заполнить дату отправки т.к. в ф-ции SetRequestMedoStatus заполняется только информация в ТЧ по статусу контрагента и информация по статусу.
          var addressee = document.Addressees.Cast<IOutgoingRequestLetterAddressees>().Where(a => Equals(a.Correspondent, company)).FirstOrDefault();
          addressee.ForwardDate = Calendar.Today;
        }
        
      }
      catch (Exception ex)
      {
        errors.Add(ex.ToString());
      }
    }
    
    /// <summary>
    /// Отправить исходящий документ в DirectumRX.
    /// </summary>
    /// <param name="document">Документ к отправке.</param>
    /// <param name="relatedDocs">Связанные документы.</param>
    /// <param name="company">Адресат.</param>
    /// <param name="errors">Ошибки при отправке.</param>
    [Public]
    public void DirRXSendToCounterparty(Sungero.Docflow.IOfficialDocument document,
                                        IQueryable<Sungero.Content.IElectronicDocument> relatedDocs,
                                        Sungero.Parties.ICounterparty correspondent,
                                        List<string> errors)
    {
      try
      {
        if (!InternalMailRegisters.GetAll(x => x.Correspondent == correspondent &&
                                          x.LeadingDocument == document &&
                                          x.Status == GD.TransmitterModule.InternalMailRegister.Status.ToProcess).Any())
        {
          var registerItem = InternalMailRegisters.Create();
          registerItem.Correspondent = correspondent;
          registerItem.LeadingDocument = document;
          
          foreach (var relatedDoc in relatedDocs)
            registerItem.RelatedDocuments.AddNew().Document = relatedDoc;
          
          registerItem.Status = GD.TransmitterModule.InternalMailRegister.Status.ToProcess;
          registerItem.Save();
        }
      }
      catch (Exception ex)
      {
        errors.Add(ex.ToString());
      }
    }
    
    /// <summary>
    /// Создать запись в реестре для перенаправления.
    /// </summary>
    /// <param name="document">Документ к отправке..</param>
    /// <param name="correspondent">Адресат.</param>
    /// <param name="errors">Ошибки при отправке.</param>
    /// <param name="IsRequestTransfer">Перенаправление обращения</param>
    /// <param name="request">Обращение для перенаправления.</param>
    [Public]
    public void CreateCounterpartyTransferRecord(Sungero.Docflow.IOfficialDocument document,
                                                 Sungero.Parties.ICounterparty correspondent,
                                                 List<string> errors,
                                                 bool IsRequestTransfer,
                                                 IRequest request)
    {
      try
      {
        if (!InternalMailRegisters.GetAll(x => x.Correspondent == correspondent &&
                                          x.LeadingDocument == document &&
                                          x.Status == GD.TransmitterModule.InternalMailRegister.Status.ToProcess).Any())
        {
          var registerItem = InternalMailRegisters.Create();
          registerItem.Correspondent = correspondent;
          registerItem.LeadingDocument = document;
          registerItem.IsRequestTransfer = IsRequestTransfer;
          registerItem.Request = request;
          registerItem.Status = GD.TransmitterModule.InternalMailRegister.Status.ToProcess;
          registerItem.Save();
        }
      }
      catch (Exception ex)
      {
        errors.Add(ex.ToString());
      }
    }
    
    /// <summary>
    /// Создать собщение в реестре отправки на e-mail.
    /// </summary>
    /// <param name="letter">Исх. письмо</param>
    /// <param name="addressee">Ссылка на строку в коллекции Адресаты</param>
    /// <param name="senderId">ИД отправителя</param>
    /// <param name="copyTo">e-mail для отправки копии</param>
    /// <param name="documentsSetId">ИД комплекта документов к отправке</param>
    public void CreateMailRegisterItem(Sungero.Docflow.IOutgoingDocumentBase letter,
                                       Sungero.Docflow.IOutgoingDocumentBaseAddressees addressee,
                                       int senderId,
                                       string documentsSetId)
    {
      if (!MailRegisters.GetAll(x => x.Correspondent == addressee.Correspondent &&
                                x.LeadingDocument == letter &&
                                x.Status == GD.TransmitterModule.MailRegister.Status.ToProcess).Any())
      {
        var registerItem = MailRegisters.Create();
        registerItem.Correspondent = addressee.Correspondent;
        if (addressee.Correspondent != null)
          registerItem.Email = addressee.Correspondent.Email;
        registerItem.LeadingDocument = letter;
        
        if (OutgoingLetters.Is(letter))
          foreach (var relatedDoc in OutgoingLetters.As(letter).DocsToSendGD)
            registerItem.RelatedDocuments.AddNew().Document = relatedDoc.Document;
        else if (OutgoingRequestLetters.Is(letter))
          foreach (var relatedDoc in OutgoingRequestLetters.As(letter).DocsToSendGD)
            registerItem.RelatedDocuments.AddNew().Document = relatedDoc.Document;
        
        registerItem.Status = GD.TransmitterModule.MailRegister.Status.ToProcess;
        registerItem.Sender = Sungero.Company.Employees.GetAll(emp => emp.Id == senderId).FirstOrDefault();
        registerItem.AddresseeId = addressee.Id;
        registerItem.DepartureDate = Calendar.Now;
        registerItem.MailType = GD.TransmitterModule.MailRegister.MailType.OutgoingLetter;
        registerItem.DocumentsSetId = documentsSetId;
        registerItem.Save();
      }
    }
    
    /// <summary>
    /// Отправить документ адресатам в рамках системы.
    /// </summary>
    /// <param name="document">Основной документ для отправки.</param>
    /// <param name="relatedDocs">Связанные документы для отправки.</param>
    public List<string> SendDocumentToAddresseesInternalMail(Sungero.Docflow.IOutgoingDocumentBase letter,
                                                             IQueryable<Sungero.Content.IElectronicDocument> relatedDocs,
                                                             bool IsRequestTransfer,
                                                             IRequest request)
    {
      Logger.DebugFormat("Debug SendDocumentToAddressees - start");
      var errors = new List<string>();
      var directumRXDeliveryMethodSid = CitizenRequests.PublicFunctions.Module.Remote.GetDirectumRXDeliveryMethodSid();
      
      if (OutgoingLetters.Is(letter))
      {
        var awaitingDispatchAddresses = letter.Addressees.Cast<IOutgoingLetterAddressees>()
          .Where(a => a.DeliveryMethod?.Sid == directumRXDeliveryMethodSid &&
                 Companies.Is(a.Correspondent) &&
                 a.DocumentState == Resources.AwaitingDispatch)
          .GroupBy(x => x.Correspondent).ToList();
        
        foreach (var item in awaitingDispatchAddresses)
        {
          var addresse = item.FirstOrDefault();
          DirRXSendToCounterparty(letter, relatedDocs, addresse.Correspondent, errors);
        }
      }
      else
      {
        var awaitingDispatchAddresses = letter.Addressees.Cast<IOutgoingRequestLetterAddressees>()
          .Where(a => a.DeliveryMethod?.Sid == directumRXDeliveryMethodSid &&
                 Companies.Is(a.Correspondent) &&
                 a.DocumentState == Resources.AwaitingDispatch)
          .ToList();
        
        foreach (var addresse in awaitingDispatchAddresses)
          CreateCounterpartyTransferRecord(letter, addresse.Correspondent, errors, IsRequestTransfer, request);
      }
      
      if (letter.State.Properties.Addressees.IsChanged)
        letter.Save();
      
      SendErrorNoticesAuthor(letter, errors);
      
      Logger.DebugFormat("Debug SendDocumentToAddressees - end");
      return errors;
    }
    
    public List<string> SendDocumentToAddresseesMedo(Sungero.Docflow.IOutgoingDocumentBase letter, IQueryable<Sungero.Content.IElectronicDocument> relatedDocs)
    {
      Logger.Debug("SendDocumentToAddresseesMedo - start");
      var errors = new List<string>();
      
      if (OutgoingLetters.Is(letter))
      {
        var awaitingDispatchAddresses = letter.Addressees.Cast<IOutgoingLetterAddressees>()
          .Where(a => a.DeliveryMethod?.Sid == MEDO.PublicConstants.Module.MedoDeliveryMethod &&
                 Companies.Is(a.Correspondent) &&
                 a.DocumentState == Resources.AwaitingDispatch)
          .GroupBy(x => x.Correspondent).ToList();
        
        foreach (var item in awaitingDispatchAddresses)
        {
          var addressee = item.FirstOrDefault();
          var company = Companies.As(addressee.Correspondent);
          MEDOSendToCounterparty(letter, relatedDocs, company, errors);
        }
      }
      else
      {
        var awaitingDispatchAddresses = letter.Addressees.Cast<IOutgoingRequestLetterAddressees>()
          .Where(a => a.DeliveryMethod?.Sid == MEDO.PublicConstants.Module.MedoDeliveryMethod &&
                 Companies.Is(a.Correspondent) &&
                 a.DocumentState == Resources.AwaitingDispatch)
          .ToList();
        
        foreach (var addresse in awaitingDispatchAddresses)
        {
          var company = Companies.As(addresse.Correspondent);
          MEDOSendToCounterparty(letter, relatedDocs, company, errors);
        }
      }
      
      if (letter.State.Properties.Addressees.IsChanged)
        letter.Save();
      
      SendErrorNoticesAuthor(letter, errors);
      Logger.DebugFormat("Debug SendDocumentToAddressees - end");
      return errors;
    }
    
    /// <summary>
    /// Отправить уведомление об ошибках автору задачи.
    /// </summary>
    /// <param name="letter"></param>
    /// <param name="errors"></param>
    public virtual void SendErrorNoticesAuthor(IOutgoingDocumentBase letter, List<string> errors)
    {

      if (errors.Count == 0)
        return;

      var allErrors = string.Join(" ", errors);
      var notice = Sungero.Workflow.SimpleTasks.CreateWithNotices(Resources.SendingDocumentsErrors, letter.Author);
      notice.Attachments.Add(letter);
      notice.ActiveText = allErrors;
      notice.Save();
      notice.Start();
      Logger.Error(allErrors);
    }

    /// <summary>
    /// Проверить реквизиты для отправки в Directum RX.
    /// </summary>
    /// <param name="document">Основной документ для отправки.</param>
    [Remote, Public]
    public List<string> CheckRequisitesForSendRX(IOutgoingLetter document)
    {
      Logger.DebugFormat("CheckRequisitesForSendRX. Начать проверку для отправки в рамках системы для документа с ИД = {0}", document.Id);
      var errors = new List<string>();
      var directumRXDeliveryMethodSid = CitizenRequests.PublicFunctions.Module.Remote.GetDirectumRXDeliveryMethodSid();
      var addresses = OutgoingLetters.As(document).Addressees.Cast<IOutgoingLetterAddressees>()
        .Where(a => a.DeliveryMethod?.Sid == directumRXDeliveryMethodSid &&
               string.IsNullOrEmpty(a.DocumentState));
      
      if (addresses.Any())
      {
        var incommingLetterDocumentKind = Sungero.Docflow.DocumentKinds.GetAll().Where(k => k.Name == Sungero.RecordManagement.Resources.IncomingLetterKindName).FirstOrDefault();
        
        foreach (var addresse in addresses)
        {
          var businessUnit = Sungero.Company.BusinessUnits.GetAll().Where(b  => Equals(b.Company, addresse.Correspondent)).OrderBy(x => x.Id).FirstOrDefault();
          
          if (businessUnit == null)
            errors.Add(Resources.CounterpartyIsNotBusinessUnitFormat(addresse.Correspondent.Name, addresse.DeliveryMethod.Name));
          else
          {
            if (businessUnit.CEO == null)
              errors.Add(Resources.BusinessUnitCEOIsEmptyFormat(addresse.Correspondent.Name));
            
            if (GetRegistrarForBusinessUnit(businessUnit, incommingLetterDocumentKind) == null)
              errors.Add(Resources.NoRegistrarInBusinessUnitFormat(addresse.Correspondent.Name, incommingLetterDocumentKind.Name));
          }
        }
      }
      
      Logger.DebugFormat("CheckRequisitesForSendRX. Завершить проверку для отправки в рамках системы для документа с ИД = {0}", document.Id);
      return errors;
    }
  }
}
