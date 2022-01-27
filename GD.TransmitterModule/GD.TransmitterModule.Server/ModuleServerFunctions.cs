using System;
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
using System.Net.Mail;

namespace GD.TransmitterModule.Server
{
  public class ModuleFunctions
  {
    
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
        var addressee = Sungero.Docflow.OutgoingDocumentBases.As(doc).Addressees
          .Where(a => a.DeliveryMethod != null && a.DeliveryMethod.Sid == PublicConstants.Module.DeliveryMethod.DirectumRX &&
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
      if (Locks.GetLockInfo(item).IsLocked)
        return;
      try
      {
        var document = Sungero.Docflow.OfficialDocuments.As(item.LeadingDocument);
        
        if (document == null)
          throw new NullReferenceException("Документ не найден.");
        
        if (Locks.GetLockInfo(document).IsLocked)
          return;
        
        var procTask = IncomingDocumentProcessingTasks.Create();
        procTask.ReasonDoc = document;
        procTask.Save();
        procTask.ToBusinessUnit = Sungero.Company.BusinessUnits.GetAll().Where(b => b.Status == Sungero.CoreEntities.DatabookEntry.Status.Active && Companies.Equals(b.Company, item.Correspondent)).FirstOrDefault();
        procTask.Save();
        procTask.ToCounterparty = item.Correspondent;
        procTask.Save();
        foreach (var relatedDoc in item.RelatedDocuments.Where(x => x.Document != null).Select(x => x.Document))
        {
          var newAddendum = procTask.Addendums.AddNew();
          newAddendum.Reason = relatedDoc;
          procTask.Save();
        }
        procTask.Save();
        procTask.Start();
        
        if (OutgoingLetters.Is(document))
        {
          var outgoingLetter = OutgoingLetters.As(document);
          
          var addressee = (IOutgoingLetterAddressees)outgoingLetter.Addressees.Where(x => x.Correspondent == item.Correspondent &&
                                                                                     x.DeliveryMethod != null &&
                                                                                     x.DeliveryMethod.Sid == Constants.Module.DeliveryMethod.DirectumRX).FirstOrDefault();
          if (addressee != null)
          {
            addressee.DocumentState = Resources.DeliveryState_Sent;
            addressee.StateInfo = Resources.DeliveryState_Sent;
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
          
          var addressee = (IOutgoingRequestLetterAddressees)outgoingRequestLetter.Addressees.Where(x => x.Correspondent == item.Correspondent &&
                                                                                                   x.DeliveryMethod != null &&
                                                                                                   x.DeliveryMethod.Sid == Constants.Module.DeliveryMethod.DirectumRX).FirstOrDefault();
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
        // Добавление возможности перенаправления входящих писем с помощью реализованного механизма.
        /*else if (IncomingLetters.Is(document))
        {
          var incomingLetter = IncomingLetters.As(document);
          
          var addressee = incomingLetter.FanSendingGD.Where(x => x.Correspondent == item.Correspondent &&
                                                            x.DeliveryMethod != null &&
                                                            x.DeliveryMethod.Sid == Constants.Module.DeliveryMethod.DirectumRX).FirstOrDefault();
          if (addressee != null)
          {
            addressee.DocumentState = Resources.DeliveryState_Sent;
          }
          
          incomingLetter.Save();
        }*/
        
        item.Status = GD.TransmitterModule.InternalMailRegister.Status.Complete;
        item.TaskId = procTask.Id;
        item.Save();
      }
      catch (Exception ex)
      {
        item.ErrorInfo = ex.Message.Substring(0, ex.Message.Length < 1000 ? ex.Message.Length : 1000);
        item.Status = GD.TransmitterModule.InternalMailRegister.Status.Error;
        item.Save();
      }
    }
    
    /// <summary>
    /// Отправка исх. писем адресатам по e-mail.
    /// </summary>
    public void SendDocumentAddresseesEMail(IMailRegister mailRegister)
    {
      var letter = OutgoingDocumentBases.As(mailRegister.LeadingDocument);
      try
      {
        var method = Sungero.Docflow.MailDeliveryMethods.GetAll(m => m.Name == Sungero.Docflow.MailDeliveryMethods.Resources.EmailMethod).FirstOrDefault();
        
        if (method == null)
        {
          throw new NullReferenceException("Не найден способ доставки по e-mail.");
        }
        
        if (!letter.HasVersions)
        {
          throw new NullReferenceException("У документа нет ни одной версии.");
        }
        
        if (Locks.GetLockInfo(letter).IsLocked)
        {
          return;
        }
        
        mailRegister.Iteration++;
        mailRegister.Save();
        
        //var isSendCopy = mailRegister.IsCopySend == true;
        var pathForDoc = string.Format(@"{0}{1}", Path.GetTempPath(), letter.Id);
        var attachmentsPath = Directory.CreateDirectory(Path.Combine(pathForDoc, Calendar.Now.ToString().Replace(" ", "").Replace(".", "").Replace(":", "")));
        var maxDocNameLength = 240 - attachmentsPath.FullName.Length;
        Logger.DebugFormat("Debug SendDocumentAddresseesEMail - 1 = " + letter.Name);
        using (var mailClient = new System.Net.Mail.SmtpClient())
        {
          using (var mail = new System.Net.Mail.MailMessage
                 {
                   Body = Resources.MailTemplateFormat(letter.BusinessUnit != null ? letter.BusinessUnit.Name : string.Empty, mailRegister.Sender, letter.BusinessUnit != null ? letter.BusinessUnit.Email : string.Empty),
                   IsBodyHtml = true,
                   Subject = Sungero.Docflow.OfficialDocuments.Resources.SendByEmailSubjectPrefixFormat(letter.Name),
                   HeadersEncoding = System.Text.Encoding.UTF8,
                   SubjectEncoding = System.Text.Encoding.UTF8,
                   BodyEncoding = System.Text.Encoding.UTF8
                 })
          {
            Logger.DebugFormat("Debug SendDocumentAddresseesEMail - 2 Add applications");
            
            var docsToSend = new List<Sungero.Content.IElectronicDocument>();
            if (OutgoingLetters.Is(letter))
              foreach (var relatedDoc in OutgoingLetters.As(letter).DocsToSendGD)
                docsToSend.Add(relatedDoc.Document);
            else if (OutgoingRequestLetters.Is(letter))
              foreach (var relatedDoc in OutgoingRequestLetters.As(letter).DocsToSendGD)
                docsToSend.Add(relatedDoc.Document);
            
            if (docsToSend.Any())
            {
              foreach (var item in docsToSend)
              {
                using (var body = item.LastVersion.Body.Read())
                {
                  using (var fileStreamPublic = File.Create(GenerateFilePath(attachmentsPath.FullName, item.Name, item.LastVersion.AssociatedApplication.Extension, maxDocNameLength)))
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
            
            if (letter.LastVersion.BodyAssociatedApplication.Extension == "pdf")
            {
              var ms = letter.LastVersion.Body.Read();
              using (var fileStreamPublic = File.Create(GenerateFilePath(attachmentsPath.FullName, letter.Name, letter.LastVersion.AssociatedApplication.Extension, maxDocNameLength)))
              {
                ms.Seek(0, SeekOrigin.Begin);
                ms.CopyTo(fileStreamPublic);
              }
            }
            else
            {
              if (!(letter.LastVersion.AssociatedApplication.Extension == "pdf"))
              {
                isNeedToConvert = true;
                // Добавление возможности автоматического преобразования отправляемого документа в pdf и установки отметки об ЭП.
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
              
              if (letter.LastVersion.AssociatedApplication.Extension == "pdf")
              {
                var ms = letter.LastVersion.PublicBody.Read();
                using (var fileStreamPublic = File.Create(GenerateFilePath(attachmentsPath.FullName, letter.Name, letter.LastVersion.AssociatedApplication.Extension, maxDocNameLength)))
                {
                  ms.Seek(0, SeekOrigin.Begin);
                  ms.CopyTo(fileStreamPublic);
                }
                
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
              else
              {
                var ms = letter.LastVersion.Body.Read();
                using (var fileStreamPublic = File.Create(GenerateFilePath(attachmentsPath.FullName, letter.Name, letter.LastVersion.AssociatedApplication.Extension, maxDocNameLength)))
                {
                  ms.Seek(0, SeekOrigin.Begin);
                  ms.CopyTo(fileStreamPublic);
                }
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
              var fileStreamSignature = File.Create(GenerateFilePath(attachmentsPath.FullName, letter.Name, "sig", maxDocNameLength));
              fileStreamSignature.Write(signatureText, 0, signatureText.Length);
              fileStreamSignature.Close();
              
              if (isPDF)
              {
                var streamOriginal = letter.LastVersion.Body.Read();
                var fileStreamOriginal = File.Create(GenerateFilePath(attachmentsPath.FullName, string.Format("{0}_original", letter.Name), letter.LastVersion.BodyAssociatedApplication.Extension, maxDocNameLength));
                streamOriginal.Seek(0, SeekOrigin.Begin);
                streamOriginal.CopyTo(fileStreamOriginal);
                fileStreamOriginal.Close();
              }
            }
            var zipName = string.Format(@"{0}\Документы_к_отправке.zip", pathForDoc);
            if (File.Exists(zipName))
            {
              File.Delete(zipName);
            }
            Logger.DebugFormat("Debug SendDocumentAddresseesEMail - 4-1 Создание архива ");
            PublicFunctions.Module.Remote.CreateFromDirectoryPublic(attachmentsPath.FullName, zipName);
            Logger.DebugFormat("Debug SendDocumentAddresseesEMail - 4-2 добавление архива во вложение ");
            if (File.Exists(zipName))
            {
              System.Net.Mail.Attachment mailAttachment = new System.Net.Mail.Attachment(zipName);
              mail.Attachments.Add(mailAttachment);
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
                mailClient.Send(mail);
                mailRegister.Status = GD.TransmitterModule.MailRegister.Status.Complete;
                mailRegister.ErrorInfo = string.Empty;
                mailRegister.Save();
              }
              catch (SmtpFailedRecipientsException ex)
              {
                isError = true;
                var errorMessage = string.Empty;
                errorMessage += ex.InnerExceptions.ToList().Select(x => x.Message) + "\n";
                mailRegister.Status = GD.TransmitterModule.MailRegister.Status.Error;
                mailRegister.ErrorInfo = errorMessage;
                mailRegister.Save();
                /*var config = DirRX.Support.PublicFunctions.Configuration.Remote.DefaultConfiguration();
                var att = new List<Sungero.Domain.Shared.IEntity>();
                att.Add(mailRegister);
                att.Add(letter);
                if (config != null && config.NotifAddressees.Any())
                  foreach (var addresseeItem in config.NotifAddressees.Select(x => x.Addressee))
                    DirRX.SiberlinkConnect.PublicFunctions.Module.Remote.SendNotification(false, addresseeItem, "Отправка почтовых сообщений. Ошибка при отправке сообщения.", errorMessage, att);*/
              }
              catch (Exception ex)
              {
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
                  (addressee as IOutgoingLetterAddressees).ForwardDateGD = Calendar.Today;
                }
                else if (OutgoingRequestLetters.Is(letter))
                {
                  (addressee as IOutgoingRequestLetterAddressees).DocumentState = Resources.DeliveryState_Sent;
                  (addressee as IOutgoingRequestLetterAddressees).ForwardDate = Calendar.Today;
                }
              }
              Logger.DebugFormat("Debug SendDocumentAddresseesEMail - 10  Finally");
            }
            letter.Save();
          }
        }
        // Удалить папку выгрузки.
        try
        {
          if (Directory.Exists(pathForDoc))
            Directory.Delete(pathForDoc, true);
        }
        catch (Exception ex)
        {
          Logger.ErrorFormat("Отправка почтовых сообщений. Ошибка при удалении папки выгрузки. {0}", ex.Message);
        }
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
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
          MEDO.PublicFunctions.Module.Remote.SetDocumentMedoStatus(Resources.DeliveryState_Sent, Resources.DeliveryState_Sent, company.MEDOGUID, OutgoingLetters.As(document), package);
        else
          MEDO.PublicFunctions.Module.Remote.SetRequestMedoStatus(Resources.DeliveryState_Sent, Resources.DeliveryState_Sent, company.MEDOGUID, OutgoingRequestLetters.As(document), package);
      }
      catch (Exception ex)
      {
        errors.Add(ex.Message);
      }
    }
    
    /// <summary>
    /// Отправка исходящего документа в DirectumRX.
    /// </summary>
    /// <param name="document">Документ к отправке.</param>
    /// <param name="relatedDocs">Связанные документы.</param>
    /// <param name="company">Адресат.</param>
    /// <returns>Ошибки при отправке.</returns>
    [Public]
    public void DirRXSendToCounterparty(Sungero.Docflow.IOfficialDocument document, IQueryable<Sungero.Content.IElectronicDocument> relatedDocs, Sungero.Parties.ICounterparty correspondent, List<string> errors)
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
        errors.Add(ex.Message);
      }
    }
    
    /// <summary>
    /// Создать собщение в реестре отправки на e-mail.
    /// </summary>
    /// <param name="letter">Исх. письмо</param>
    /// <param name="addressee">Ссылка на строку в коллекции Адресаты</param>
    /// <param name="sender">Отправитель</param>
    /// <param name="copyTo">e-mail для отправки копии</param>
    public void CreateMailRegisterItem(Sungero.Docflow.IOutgoingDocumentBase letter,
                                       Sungero.Docflow.IOutgoingDocumentBaseAddressees addressee,
                                       string sender)
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
        registerItem.Sender = sender;
        registerItem.AddresseeId = addressee.Id;
        registerItem.DepartureDate = Calendar.Now;
        registerItem.MailType = GD.TransmitterModule.MailRegister.MailType.OutgoingLetter;
        registerItem.Save();
      }
    }
    
    /// <summary>
    /// Отправка документа адресатам.
    /// </summary>
    /// <param name="document">Основной документ для отправки.</param>
    /// <param name="relatedDocs">Связанные документы для отправки.</param>
    public List<string> SendDocumentToAddressees(Sungero.Docflow.IOutgoingDocumentBase letter, IQueryable<Sungero.Content.IElectronicDocument> relatedDocs)
    {
      Logger.DebugFormat("Debug SendDocumentToAddressees - start");
      var errors = new List<string>();
      if (OutgoingLetters.Is(letter))
      {
        var awaitingDispatchAddresses = letter.Addressees.Cast<IOutgoingLetterAddressees>()
          .Where(a => a.DeliveryMethod != null)
          .Where(a => Companies.Is(a.Correspondent) &&
                 a.DocumentState == Resources.AwaitingDispatch &&
                 (a.DeliveryMethod.Sid == MEDO.PublicConstants.Module.MedoDeliveryMethod ||
                  a.DeliveryMethod.Sid == PublicConstants.Module.DeliveryMethod.DirectumRX))
          .GroupBy(x => x.Correspondent).ToList();
        foreach (var item in awaitingDispatchAddresses)
        {
          var addresse = item.FirstOrDefault();
          var deliveryMethodSid = addresse.DeliveryMethod.Sid;
          if (deliveryMethodSid == MEDO.PublicConstants.Module.MedoDeliveryMethod)
          {
            var company = Companies.As(addresse.Correspondent);
            MEDOSendToCounterparty(letter, relatedDocs, company, errors);
          }
          else if (deliveryMethodSid == PublicConstants.Module.DeliveryMethod.DirectumRX)
            DirRXSendToCounterparty(letter, relatedDocs, addresse.Correspondent, errors);
        }
      }
      else
      {
        var awaitingDispatchAddresses = letter.Addressees.Cast<IOutgoingRequestLetterAddressees>()
          .Where(a => a.DeliveryMethod != null)
          .Where(a => Companies.Is(a.Correspondent) &&
                 a.DocumentState == Resources.AwaitingDispatch &&
                 (a.DeliveryMethod.Sid == MEDO.PublicConstants.Module.MedoDeliveryMethod ||
                  a.DeliveryMethod.Sid == PublicConstants.Module.DeliveryMethod.DirectumRX))
          .ToList();
        foreach (var addresse in awaitingDispatchAddresses)
        {
          var deliveryMethodSid = addresse.DeliveryMethod.Sid;
          if (deliveryMethodSid == MEDO.PublicConstants.Module.MedoDeliveryMethod)
          {
            var company = Companies.As(addresse.Correspondent);
            MEDOSendToCounterparty(letter, relatedDocs, company, errors);
          }
          else if (deliveryMethodSid == PublicConstants.Module.DeliveryMethod.DirectumRX)
            DirRXSendToCounterparty(letter, relatedDocs, addresse.Correspondent, errors);
        }
      }
      if (letter.State.Properties.Addressees.IsChanged)
        letter.Save();
      if (errors.Count() > 0)
      {
        var allErrors = string.Empty;
        foreach (var error in errors)
          allErrors = string.Format("{0} {1}", error, allErrors);

        var notice = Sungero.Workflow.SimpleTasks.CreateWithNotices(Resources.SendingDocumentsErrors, letter.Author);
        notice.Attachments.Add(letter);
        notice.ActiveText = allErrors;
        notice.Save();
        notice.Start();
        Logger.Error(allErrors);
      }
      Logger.DebugFormat("Debug SendDocumentToAddressees - end");
      return errors;
    }

    /// <summary>
    /// Проверить реквизиты для отправки в Directum RX.
    /// </summary>
    /// <param name="document">Основной документ для отправки.</param>
    [Remote, Public]
    public List<string> CheckRequisitesForSendRX(Sungero.Docflow.IOfficialDocument document)
    {
      Logger.DebugFormat("Debug CheckRequisitesForSendRX - start");
      var errors = new List<string>();
      if (OutgoingLetters.Is(document))
      {
        var addresses = OutgoingLetters.As(document).Addressees.Cast<IOutgoingLetterAddressees>()
          .Where(a => a.DeliveryMethod != null)
          .Where(a => a.DeliveryMethod.Sid == PublicConstants.Module.DeliveryMethod.DirectumRX &&
                 string.IsNullOrEmpty(a.DocumentState));
        Logger.DebugFormat("Debug CheckRequisitesForSendRX - 1");
        if (addresses.Count() > 0)
        {
          Logger.DebugFormat("Debug CheckRequisitesForSendRX - 2");
          var incommingLetterDocumentKind = Sungero.Docflow.DocumentKinds.GetAll().Where(k => k.Name == Sungero.RecordManagement.Resources.IncomingLetterKindName).FirstOrDefault();
          Logger.DebugFormat("!!! CheckRequisitesForSendRX - 3");
          foreach (var addresse in addresses)
          {
            Logger.DebugFormat("Debug CheckRequisitesForSendRX - 4");
            var businessUnit = Sungero.Company.BusinessUnits.GetAll().Where(b  => Equals(b.Company, addresse.Correspondent)).OrderBy(x => x.Id).FirstOrDefault();
            Logger.DebugFormat("Debug CheckRequisitesForSendRX - 5");
            if (businessUnit == null)
              errors.Add(Resources.CounterpartyIsNotBusinessUnitFormat(addresse.Correspondent.Name, addresse.DeliveryMethod.Name));
            else
            {
              Logger.DebugFormat("Debug CheckRequisitesForSendRX - 6");
              if (GetRegistrarForBusinessUnit(businessUnit, incommingLetterDocumentKind) == null)
                errors.Add(Resources.NoRegistrarInBusinessUnitFormat(addresse.Correspondent.Name, incommingLetterDocumentKind.Name));
            }
          }
        }
      }
      else if (OutgoingRequestLetters.Is(document))
      {
        var addresses = OutgoingRequestLetters.As(document).Addressees.Cast<IOutgoingRequestLetterAddressees>()
          .Where(a => a.DeliveryMethod != null)
          .Where(a => a.DeliveryMethod.Sid == PublicConstants.Module.DeliveryMethod.DirectumRX &&
                 string.IsNullOrEmpty(a.DocumentState));
        Logger.DebugFormat("Debug CheckRequisitesForSendRX - 1");
        if (addresses.Count() > 0)
        {
          Logger.DebugFormat("Debug CheckRequisitesForSendRX - 2");
          var addressesName = string.Join(", ", addresses.Select(a => a.Correspondent.Name));
          errors.Add(Resources.UseReferralByCompetenceFormat(addressesName));
        }
      }
      // Добавление возможности перенаправления входящих писем с помощью реализованного механизма.
      /*else if (IncomingLetters.Is(document))
      {
        var addresses = IncomingLetters.As(document).FanSendingGD
          .Where(a => a.DeliveryMethod != null)
          .Where(a => a.DeliveryMethod.Sid == PublicConstants.Module.DeliveryMethod.DirectumRX &&
                 string.IsNullOrEmpty(a.DocumentState));
        Logger.DebugFormat("!!! CheckRequisitesForSendRX - 1");
        if (addresses.Count() > 0)
        {
          Logger.DebugFormat("!!! CheckRequisitesForSendRX - 2");
          var incommingLetterDocumentKind = Sungero.Docflow.DocumentKinds.GetAll().Where(k => k.Name == Sungero.RecordManagement.Resources.IncomingLetterKindName).FirstOrDefault();
          Logger.DebugFormat("!!! CheckRequisitesForSendRX - 3");
          foreach (var addresse in addresses)
          {
            Logger.DebugFormat("!!! CheckRequisitesForSendRX - 4");
            var businessUnit = Sungero.Company.BusinessUnits.GetAll().Where(b  => Equals(b.Company, addresse.Correspondent)).OrderBy(x => x.Id).FirstOrDefault();
            Logger.DebugFormat("!!! CheckRequisitesForSendRX - 5");
            if (businessUnit == null)
              errors.Add(Resources.CounterpartyIsNotBusinessUnitFormat(addresse.Correspondent.Name, addresse.DeliveryMethod.Name));
            else
            {
              Logger.DebugFormat("!!! CheckRequisitesForSendRX - 6");
              if (GetRegistrarForBusinessUnit(businessUnit, incommingLetterDocumentKind) == null)
                errors.Add(Resources.NoRegistrarInBusinessUnitFormat(addresse.Correspondent.Name, incommingLetterDocumentKind.Name));
            }
          }
        }
      }*/
      Logger.DebugFormat("Debug CheckRequisitesForSendRX - end");
      return errors;
    }

    /// <summary>
    /// Проверить реквизиты для отправки по Email.
    /// </summary>
    /// <param name="document">Основной документ для отправки.</param>
    [Remote, Public]
    public List<string> CheckRequisitesForEmail(Sungero.Docflow.IOfficialDocument document)
    {
      Logger.DebugFormat("Debug CheckRequisitesForEmail - start");
      var errors = new List<string>();
      if (OutgoingLetters.Is(document))
      {
        var method = Sungero.Docflow.MailDeliveryMethods.GetAll(m => m.Name == Sungero.Docflow.MailDeliveryMethods.Resources.EmailMethod).FirstOrDefault();
        var addresses = OutgoingLetters.As(document).Addressees.Cast<IOutgoingLetterAddressees>()
          .Where(a => a.DeliveryMethod != null)
          .Where(a => a.DeliveryMethod == method && string.IsNullOrEmpty(a.DocumentState));
        Logger.DebugFormat("Debug CheckRequisitesForEmail - 1");
        if (addresses.Count() > 0)
        {
          foreach (var addresse in addresses)
          {
            var email = addresse.Correspondent.Email;
            if (string.IsNullOrEmpty(email))
              errors.Add(GD.TransmitterModule.Resources.CounterpartyIsNotEmailFormat(addresse.Correspondent.Name));
          }
        }
      }
      Logger.DebugFormat("Debug CheckRequisitesForEmail - end");
      return errors;
    }

    
    /// <summary>
    /// Вызов обработчика для отправки исходящего письма адресатам.
    /// </summary>
    /// <param name="document">Основной документ для отправки.</param>
    [Public, Remote]
    public void SendingDocumentAsyncHandlers(Sungero.Docflow.IOfficialDocument document)
    {
      Logger.DebugFormat("Debug SendingDocumentAsyncHandlers - 1-1");
      var relatedDocumentsIds = string.Empty;
      if (OutgoingLetters.Is(document))
        relatedDocumentsIds = string.Join(",", OutgoingLetters.As(document).DocsToSendGD.Where(d => d.Document != null).Select(d => d.Document.Id).ToList());
      else if (OutgoingRequestLetters.Is(document))
        relatedDocumentsIds = string.Join(",", OutgoingRequestLetters.As(document).DocsToSendGD.Where(d => d.Document != null).Select(d => d.Document.Id).ToList());
      // Добавление возможности перенаправления входящих писем с помощью реализованного механизма.
      /*else if (IncomingLetters.Is(document))
        relatedDocumentsIds = string.Join(",", IncomingLetters.As(document).DocsToSendGD.Where(d => d.Document != null).Select(d => d.Document.Id).ToList());*/
      Logger.DebugFormat("Debug SendingDocumentAsyncHandlers - 1-2");
      var asyncSendingHandler = AsyncHandlers.SendDocumentToAddressees.Create();
      Logger.DebugFormat("Debug SendingDocumentAsyncHandlers - 1-3");
      asyncSendingHandler.DocumentID = document.Id;
      asyncSendingHandler.RelationDocumentIDs = relatedDocumentsIds;
      asyncSendingHandler.ExecuteAsync();
      Logger.DebugFormat("Debug SendingDocumentAsyncHandlers - 1-4");
    }
  }
}
