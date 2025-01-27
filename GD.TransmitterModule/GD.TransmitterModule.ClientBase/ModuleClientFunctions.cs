using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;
using Sungero.Docflow;
using GD.GovernmentSolution;
using GD.CitizenRequests;

namespace GD.TransmitterModule.Client
{
  public class ModuleFunctions
  {

    /// <summary>
    /// Открыть карточку настроек модуля.
    /// </summary>
    [Public]
    public virtual void ShowTransmitterSettings()
    {
      PublicFunctions.Module.Remote.GetTransmitterSettings().Show();
    }
    
    /// <summary>
    /// Проверить расширения приложений для МЭДО
    /// </summary>
    /// <param name="document">Документ.</param>
    [Public]
    public static bool CheckExtForMedo(Sungero.Docflow.IOfficialDocument document)
    {
      var incorrectExtension = false;
      var incorrectExtensionLength = false;
      
      if (OutgoingLetters.Is(document))
      {
        var outDocLet = OutgoingLetters.As(document);
        
        foreach (var addendum in outDocLet.DocsToSendGD)
        {
          var docAdd = addendum.Document;
          var ext = docAdd.LastVersion.AssociatedApplication.Extension.ToLower();
          
          // отсеивание файлов с длиной расширения меньше 3 или больше 4 символов осуществляетсяв разделяемой функции StartStartSendingDocumentToAddresseesMedo
          if (ext.Length < 3 || ext.Length > 4)
          {
            incorrectExtension = true;
            incorrectExtensionLength = true;
            break;
          }
          if (ext != "pdf" && ext != "tif" && ext != "doc" && ext != "txt" && ext != "xml")
            incorrectExtension = true;
        }
      }
      else
      {
        var outDocReqLet = OutgoingRequestLetters.As(document);
        foreach (var addendum in outDocReqLet.DocsToSendGD)
        {
          var docAdd = addendum.Document;
          var ext = docAdd.LastVersion.AssociatedApplication.Extension.ToLower();
          
          if (ext.Length < 3 || ext.Length > 4)
          {
            incorrectExtension = true;
            incorrectExtensionLength = true;
            break;
          }
          if (ext != "pdf" && ext != "tif" && ext != "doc" && ext != "txt" && ext != "xml")
            incorrectExtension = true;
        }
      }
      
      if (incorrectExtension || incorrectExtensionLength)
      {
        var dialogText = Resources.NeedCorrectExt;
        if (incorrectExtensionLength)
          dialogText = Resources.IncorrectExtLength + dialogText;
        
        var confirmationDialog = Dialogs.CreateTaskDialog(string.Empty,
                                                          dialogText,
                                                          MessageType.Question);
        var abort = confirmationDialog.Buttons.AddCustom(Resources.Abort);
        confirmationDialog.Buttons.Default = abort;
        var notAbort = confirmationDialog.Buttons.AddCustom(Resources.NotAbort);
        confirmationDialog.Buttons.AddCancel();
        var result = confirmationDialog.Show();
        
        // Необходимость прекращения подчиненных поручений.
        if (result == abort || result == DialogButtons.Cancel)
          return true;
      }
      return false;
    }
    
    /// <summary>
    /// Проверить возможность отправки исх. письма адресатам.
    /// </summary>
    /// <param name="document">Исходящее письмо.</param>
    [Public]
    public virtual bool CanSendToAddressee(IOutgoingLetter document)
    {
      return document.Correspondent != null && document.RegistrationState == Sungero.Docflow.OfficialDocument.RegistrationState.Registered &&
        document.HasVersions && !document.State.IsChanged;
    }

    /// <summary>
    /// Отправить исх. письмо и связанные с ним документы адресатам.
    /// </summary>
    /// <param name="document">Исходящее письмо.</param>
    [Public]
    public virtual Structures.Module.ISendToAddresseeResult SendToAddressee(IOutgoingLetter document)
    {
      // Выбрать связанные документы для отправки.
      var allRelatedDocuments = new List<Sungero.Content.IElectronicDocument>();;
      var existSending = false;
      
      foreach (var relationName in RelationTypes.GetAll().Select(r => r.Name))
      {
        allRelatedDocuments.AddRange(document.Relations
                                     .GetRelatedAndRelatedFromDocuments(relationName)
                                     .Where(d => d.HasVersions)
                                     .OrderBy(d => d.Name));
      }
      
      Logger.DebugFormat("SendToAddressee. Количество связаннных документов = {0} для документа с Id = {1}", allRelatedDocuments.Count, document.Id);
      
      if (allRelatedDocuments.Any())
      {
        var dialogSelectDocument = Dialogs.CreateInputDialog(Resources.RelatedDocumentsForSending);
        var applications = dialogSelectDocument.AddSelectMany(Resources.SelectDocuments, false, Sungero.Content.ElectronicDocuments.Null).
          From(allRelatedDocuments.Distinct().ToArray());
        
        if (dialogSelectDocument.Show() == DialogButtons.Ok)
        {
          document.DocsToSendGD.Clear();
          
          foreach (var relatedDoc in applications.Value)
          {
            var newRelatedDoc = document.DocsToSendGD.AddNew();
            newRelatedDoc.Document = relatedDoc;
          }
          
          if (document.DocsToSendGD.Any())
            document.Save();
        }
        else
        {
          return null;
        }
      }
      
      var information = new List<string>();
      
      // Отправка адресатам по E-mail.
      var methodEmail = Sungero.Docflow.MailDeliveryMethods.GetAll(m => m.Name == Sungero.Docflow.MailDeliveryMethods.Resources.EmailMethod).FirstOrDefault();
      
      if (methodEmail == null)
        AppliedCodeException.Create(GD.TransmitterModule.Resources.EmailMethodNotFound);
      
      var errorsEmail = new List<string>();
      var addresseesEmail = document.Addressees.Cast<IOutgoingLetterAddressees>().Where(x => Equals(x.DeliveryMethod, methodEmail) && string.IsNullOrEmpty(x.DocumentState));
      var needStartSendingDocumentToAddresseesEMail = false;
      
      if (addresseesEmail.Any())
      {
        Logger.DebugFormat("SendToAddressee. Проверить реквизиты для отправки по Email для документа с ИД = {0}", document.Id);
        errorsEmail.AddRange(PublicFunctions.Module.CheckRequisitesForEmail(document, methodEmail));
        var settings = Functions.Module.Remote.GetTransmitterSettings();
        
        if (settings.MaxAttachmentFileSize.HasValue && !Functions.Module.Remote.CheckPackageSize(document, settings.MaxAttachmentFileSize.Value))
          errorsEmail.Add(GD.TransmitterModule.Resources.GeneratedArchiveExceedsMaxSizeFormat(settings.MaxAttachmentFileSize.Value));
        
        if (!errorsEmail.Any())
        {
          foreach(var addressee in addresseesEmail)
          {
            addressee.DocumentState = Resources.AwaitingDispatch;
            addressee.AddresserGD = Users.Current;
          }
          needStartSendingDocumentToAddresseesEMail = true;
          existSending = true;
        }
      }
      
      // Проверки для отправки по МЭДО.
      var errorsMEDO = new List<string>();
      var methodMedo = Sungero.Docflow.MailDeliveryMethods.GetAll(m => m.Sid == MEDO.PublicConstants.Module.MedoDeliveryMethod).FirstOrDefault();
      
      if (methodMedo == null)
        AppliedCodeException.Create(GD.TransmitterModule.Resources.MethodMedoNotFound);
      
      var addressesMedo = document.Addressees.Cast<IOutgoingLetterAddressees>().Where(a => Equals(a.DeliveryMethod, methodMedo) && string.IsNullOrEmpty(a.DocumentState));
      var needStartStartSendingDocumentToAddresseesMedo = false;
      
      if (addressesMedo.Any())
      {
        Logger.DebugFormat("SendToAddressee. Проверить реквизиты для отправки по МЭДО для документа с ИД = {0}", document.Id);
        errorsMEDO = MEDO.PublicFunctions.Module.Remote.CheckRequisites(document, false);
        if (document.PreparedBy.IsSystem == true)
        {
          if (Sungero.Company.Employees.Current != null)
          {
            errorsMEDO.Remove(Resources.PreparedByPropertiesAreEmpty);
            
            if (Sungero.Company.Employees.Current.JobTitle == null || string.IsNullOrEmpty(Sungero.Company.Employees.Current.Phone))
              errorsMEDO.Add(Resources.CurrentEmployeePropertiesAreEmpty);
          }
          else
          {
            errorsMEDO.Add(Resources.PreparedByIsSystem);
          }
        }
        
        if (MEDO.PublicFunctions.Module.Remote.HasCounterparty27MedoFormat(document))
          if (!Sungero.RecordManagement.PublicFunctions.AcquaintanceTask.Remote.IsDocumentVersionReaded(document, document.LastVersion.Number.Value))
            errorsMEDO.Add(GovernmentSolution.OutgoingLetters.Resources.NeedViewStampFormat(document.Info.Actions.ReadVersion));

        // Проверка расширений приложений.
        var needCancel = PublicFunctions.Module.CheckExtForMedo(document);
        
        if (needCancel)
        {
          Logger.DebugFormat("SendToAddressee. Расширение основного документа или связанных документов не соответствует формату для отправки по МЭДО, ИД документа = {0}", document.Id);
          return null;
        }
        
        var responseDoc = document.InResponseTo;
        
        if (GovernmentSolution.IncomingLetters.As(responseDoc) != null)
        {
          var signedBy = GovernmentSolution.IncomingLetters.As(responseDoc).SignedBy;
          
          if (GovernmentSolution.IncomingLetters.As(responseDoc).SignedBy == null)
            errorsMEDO.Add(Resources.NeedEnterSignedByFormat(responseDoc.Name));
        }
        
        if (document.AddendumsPageCount == null)
          errorsMEDO.Add(Resources.FillInAddendumsPageCount);
        
        if (!errorsMEDO.Any())
        {
          var sender = Users.Current.IsSystem == true ? null : Users.Current;
          
          foreach (var addresse in addressesMedo)
          {
            addresse.DocumentState = Resources.AwaitingDispatch;
            addresse.AddresserGD = sender;
          }
          needStartStartSendingDocumentToAddresseesMedo = true;
          existSending = true;
        }
      }
      
      // Проверки для отправки в Directum RX.
      var errorsRX = new List<string>();
      
      // Если проверки для отправки не пройдены - не менять статус для адресатов.
      var directumRXDeliveryMethodSid = CitizenRequests.PublicFunctions.Module.Remote.GetDirectumRXDeliveryMethodSid();
      var addressesTransfer = document.Addressees.Cast<IOutgoingLetterAddressees>().Where(a => a.DeliveryMethod?.Sid == directumRXDeliveryMethodSid && string.IsNullOrEmpty(a.DocumentState));
      var needStartInternalSendingDocuments = false;
      
      if (addressesTransfer.Any())
      {
        Logger.DebugFormat("SendToAddressee. Проверить реквизиты для отправки в рамках системы для документа с ИД = {0}", document.Id);
        errorsRX = PublicFunctions.Module.Remote.CheckRequisitesForSendRX(document);
        
        if (!errorsRX.Any())
        {
          foreach (var addresse in addressesTransfer)
          {
            addresse.DocumentState = Resources.AwaitingDispatch;
            addresse.AddresserGD = Users.Current.IsSystem == true ? null : Users.Current;
          }
          needStartInternalSendingDocuments = true;
          existSending = true;
        }
      }
      
      // TODO. Чтобы запись в истории отображалась корректно, название действия (переменная operation) необходимо локализовать
      // (создать ресурс в необходимом типе документа с названием Enum_Operation_<значение перечисления>. Например, Enum_Operation_SendAddressees)
      if (existSending)
      {
        Functions.Module.Remote.WriteSendingDocsInHistory(document);
        
        if (document.IsManyAddressees == false)
          document.DocumentState = Resources.AwaitingDispatch;
        
        document.Save();
        information.Add(Resources.DocumentWasSending);
      }
      
      // Запустить АО после сохранения карточки, для того чтобы в фильтрацию попали адресаты со статусом отправки "Ожидает отправки".
      if (needStartSendingDocumentToAddresseesEMail)
      {
        Logger.DebugFormat("SendToAddressee. Стартовать АО для отправки документа адресатам по Email, ИД документа = {0}", document.Id);
        Functions.Module.StartSendingDocumentToAddresseesEMail(document);
      }
      
      if (needStartStartSendingDocumentToAddresseesMedo)
      {
        var sender = Users.Current.IsSystem == true ? null : Users.Current;
        Logger.DebugFormat("SendToAddressee. Стартовать АО для отправки документа адресатам по МЭДО, ИД документа = {0}", document.Id);
        Functions.Module.StartStartSendingDocumentToAddresseesMedo(document, sender);
      }
      
      if (needStartInternalSendingDocuments)
      {
        Logger.DebugFormat("SendToAddressee. Стартовать АО для отправки документа адресатам в рамках системы, ИД документа = {0}", document.Id);
        PublicFunctions.Module.StartInternalSendingDocuments(document);
      }
      
      var addresseesWithoutDeliveryMethod = document.Addressees.Where(a => a.DeliveryMethod == null);
      
      foreach (var addressee in addresseesWithoutDeliveryMethod)
        errorsRX.Add(GD.TransmitterModule.Resources.CorrespondentDeliveryMethodIsEmptyFormat(addressee.Correspondent.Name));
      
      return Structures.Module.SendToAddresseeResult.Create(information, errorsRX, errorsMEDO, errorsEmail);
    }

    /// <summary>
    /// Отправить исх. письмо по обращению и связанные с ним документы адресатам.
    /// </summary>
    /// <param name="document">Исходящее письмо.</param>
    [Public]
    public virtual Structures.Module.ISendToAddresseeResult SendToAddressee(IOutgoingRequestLetter document)
    {
      if (!CitizenRequests.PublicFunctions.OutgoingRequestLetter.IsTransfer(document))
      {
        var allRelatedDocuments = new List<Sungero.Content.IElectronicDocument>();;
        
        foreach (var relationName in RelationTypes.GetAll().Select(r => r.Name))
        {
          allRelatedDocuments.AddRange(document.Relations.GetRelatedAndRelatedFromDocuments(relationName)
                                       .Where(d => d.HasVersions)
                                       .OrderBy(d => d.Name));
        }
        
        Logger.DebugFormat("SendToAddressee. Количество связаннных документов = {0} для документа с Id = {1}", allRelatedDocuments.Count, document.Id);
        
        if (allRelatedDocuments.Any())
        {
          var dialogSelectDocument = Dialogs.CreateInputDialog(Resources.RelatedDocumentsForSending);
          var applications = dialogSelectDocument.AddSelectMany(Resources.SelectDocuments, false, Sungero.Content.ElectronicDocuments.Null).
            From(allRelatedDocuments.Distinct().ToArray());
          
          if (dialogSelectDocument.Show() == DialogButtons.Ok)
          {
            document.DocsToSendGD.Clear();
            
            foreach (var relatedDoc in applications.Value)
            {
              var newRelatedDoc = document.DocsToSendGD.AddNew();
              newRelatedDoc.Document = relatedDoc;
            }
            
            if (document.DocsToSendGD.Any())
              document.Save();
          }
          else
          {
            return null;
          }
        }
      }
      
      var existSending = false;
      var information = new List<string>();
      
      // Отправка адресатам по E-mail.
      var methodEmail = Sungero.Docflow.MailDeliveryMethods.GetAll(m => m.Name == Sungero.Docflow.MailDeliveryMethods.Resources.EmailMethod).FirstOrDefault();
      
      if (methodEmail == null)
        AppliedCodeException.Create(GD.TransmitterModule.Resources.EmailMethodNotFound);
      
      var errorsEmail = new List<string>();
      var addresseesEmail = document.Addressees.Cast<IOutgoingRequestLetterAddressees>().Where(x => Equals(x.DeliveryMethod, methodEmail) && string.IsNullOrEmpty(x.DocumentState));
      var needStartSendingDocumentToAddresseesEMail = false;
      
      if (addresseesEmail.Any())
      {
        Logger.DebugFormat("SendToAddressee. Проверить реквизиты для отправки по Email для документа с ИД = {0}", document.Id);
        errorsEmail.AddRange(PublicFunctions.Module.CheckRequisitesForEmail(document, methodEmail));
        var settings = Functions.Module.Remote.GetTransmitterSettings();
        
        if (settings.MaxAttachmentFileSize.HasValue && !Functions.Module.Remote.CheckPackageSize(document, settings.MaxAttachmentFileSize.Value))
          errorsEmail.Add(GD.TransmitterModule.Resources.GeneratedArchiveExceedsMaxSizeFormat(settings.MaxAttachmentFileSize.Value));
        
        if (!errorsEmail.Any())
        {
          foreach(var addressee in addresseesEmail)
          {
            addressee.DocumentState = Resources.AwaitingDispatch;
            addressee.Addresser = Users.Current;
          }
          needStartSendingDocumentToAddresseesEMail = true;
          existSending = true;
        }
      }
      
      // Проверки для отправки по МЭДО.
      var errorsMEDO = new List<string>();
      var methodMedo = Sungero.Docflow.MailDeliveryMethods.GetAll(m => m.Sid == MEDO.PublicConstants.Module.MedoDeliveryMethod).FirstOrDefault();
      
      if (methodMedo == null)
        AppliedCodeException.Create(GD.TransmitterModule.Resources.MethodMedoNotFound);
      
      var addressesMedo = document.Addressees.Cast<IOutgoingRequestLetterAddressees>().Where(a => Equals(a.DeliveryMethod, methodMedo) && string.IsNullOrEmpty(a.DocumentState));
      var needStartStartSendingDocumentToAddresseesMedo = false;
      
      if (addressesMedo.Any())
      {
        Logger.DebugFormat("SendToAddressee. Проверить реквизиты для отправки по МЭДО для документа с ИД = {0}", document.Id);
        errorsMEDO = MEDO.PublicFunctions.Module.Remote.CheckRequisites(document, true);
        
        if (document.PreparedBy.IsSystem == true)
        {
          if (Sungero.Company.Employees.Current != null)
          {
            errorsMEDO.Remove(Resources.PreparedByPropertiesAreEmpty);
            
            if (Sungero.Company.Employees.Current.JobTitle == null || string.IsNullOrEmpty(Sungero.Company.Employees.Current.Phone))
              errorsMEDO.Add(Resources.CurrentEmployeePropertiesAreEmpty);
          }
          else
          {
            errorsMEDO.Add(Resources.PreparedByIsSystem);
          }
        }
        
        if (MEDO.PublicFunctions.Module.Remote.HasCounterparty27MedoFormat(document))
          if (!Sungero.RecordManagement.PublicFunctions.AcquaintanceTask.Remote.IsDocumentVersionReaded(document, document.LastVersion.Number.Value))
            errorsMEDO.Add(GovernmentSolution.OutgoingLetters.Resources.NeedViewStampFormat(document.Info.Actions.ReadVersion));

        // Проверка расширений приложений.
        var needCancel = PublicFunctions.Module.CheckExtForMedo(document);
        
        if (needCancel)
        {
          Logger.DebugFormat("SendToAddressee. Расширение основного документа или связанных документов не соответствует формату для отправки по МЭДО, ИД документа = {0}", document.Id);
          return null;
        }
        
        var responseDoc = document.InResponseTo;
        
        if (GovernmentSolution.IncomingLetters.As(responseDoc) != null)
        {
          var signedBy = GovernmentSolution.IncomingLetters.As(responseDoc).SignedBy;
          
          if (GovernmentSolution.IncomingLetters.As(responseDoc).SignedBy == null)
            errorsMEDO.Add(Resources.NeedEnterSignedByFormat(responseDoc.Name));
        }
        
        if (document.AddendumsPageCount == null)
          errorsMEDO.Add(Resources.FillInAddendumsPageCount);
        
        if (!errorsMEDO.Any())
        {
          var sender = Users.Current.IsSystem == true ? null : Users.Current;
          
          foreach (var addresse in addressesMedo)
          {
            addresse.DocumentState = Resources.AwaitingDispatch;
            addresse.Addresser = sender;
          }
          needStartStartSendingDocumentToAddresseesMedo = true;
          existSending = true;
        }
      }
      
      var errorsTransfer = new List<string>();
      var directumRXDeliveryMethodSid = CitizenRequests.PublicFunctions.Module.Remote.GetDirectumRXDeliveryMethodSid();
      var addressesTransfer = document.Addressees.Cast<IOutgoingRequestLetterAddressees>().Where(a => a.DeliveryMethod?.Sid == directumRXDeliveryMethodSid && string.IsNullOrEmpty(a.DocumentState));
      var needStartInternalSendingDocuments = false;
      
      // Проверки для отправки перенаправлением.
      if (addressesTransfer.Any())
      {
        Logger.DebugFormat("SendToAddressee. Проверить реквизиты для отправки в рамках системы для документа с ИД = {0}", document.Id);
        errorsTransfer = CitizenRequests.PublicFunctions.Module.Remote.CheckRequisitesForInternalTransfer(document);
        
        if (!errorsTransfer.Any())
        {
          foreach (var addresse in addressesTransfer)
          {
            addresse.DocumentState = Resources.AwaitingDispatch;
            addresse.Addresser = Users.Current;
          }
          needStartInternalSendingDocuments = true;
          existSending = true;
        }
      }
      
      // TODO. Чтобы запись в истории отображалась корректно, название действия (переменная operation) необходимо локализовать
      // (создать ресурс в необходимом типе документа с названием Enum_Operation_<значение перечисления>. Например, Enum_Operation_SendAddressees)
      if (existSending)
      {
        Functions.Module.Remote.WriteSendingDocsInHistory(document);
        
        if (document.IsManyAddressees == false)
          document.DocumentState = Resources.AwaitingDispatch;
        
        document.Save();
        information.Add(Resources.DocumentWasSending);
      }
      
      // Запустить АО после сохранения карточки, для того чтобы в фильтрацию попали адресаты со статусом отправки "Ожидает отправки".
      if (needStartSendingDocumentToAddresseesEMail)
      {
        Logger.DebugFormat("SendToAddressee. Стартовать АО для отправки документа адресатам по Email, ИД документа = {0}", document.Id);
        Functions.Module.StartSendingDocumentToAddresseesEMail(document);
      }
      
      if (needStartStartSendingDocumentToAddresseesMedo)
      {
        var sender = Users.Current.IsSystem == true ? null : Users.Current;
        Logger.DebugFormat("SendToAddressee. Стартовать АО для отправки документа адресатам по МЭДО, ИД документа = {0}", document.Id);
        Functions.Module.StartStartSendingDocumentToAddresseesMedo(document, sender);
      }
      
      if (needStartInternalSendingDocuments)
      {
        Logger.DebugFormat("SendToAddressee. Стартовать АО для отправки документа адресатам в рамках системы, ИД документа = {0}", document.Id);
        PublicFunctions.Module.StartInternalSendingDocuments(document);
      }
      
      var addresseesWithoutDeliveryMethod = document.Addressees.Where(a => a.DeliveryMethod == null);
      
      foreach (var addressee in addresseesWithoutDeliveryMethod)
        errorsTransfer.Add(GD.TransmitterModule.Resources.CorrespondentDeliveryMethodIsEmptyFormat(addressee.Correspondent.Name));
      
      return Structures.Module.SendToAddresseeResult.Create(information, errorsTransfer, errorsMEDO, errorsEmail);
    }
  }
}