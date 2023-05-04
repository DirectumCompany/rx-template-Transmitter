﻿using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;
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
      bool needDialog = false;
      
      if (OutgoingLetters.Is(document))
      {
        var outDocLet = OutgoingLetters.As(document);
        foreach (var addendum in outDocLet.DocsToSendGD)
        {
          var docAdd = addendum.Document;
          var ext = docAdd.LastVersion.AssociatedApplication.Extension.ToLower();
          if (ext != "pdf" && ext != "tif" && ext != "doc" && ext != "txt" && ext != "xml")
            needDialog = true;
        }
      }
      else
      {
        var outDocReqLet = OutgoingRequestLetters.As(document);
        foreach (var addendum in outDocReqLet.DocsToSendGD)
        {
          var docAdd = addendum.Document;
          var ext = docAdd.LastVersion.AssociatedApplication.Extension.ToLower();
          if (ext != "pdf" && ext != "tif" && ext != "doc" && ext != "txt" && ext != "xml")
            needDialog = true;
        }
      }
      
      if (needDialog)
      {
        var confirmationDialog = Dialogs.CreateTaskDialog(string.Empty,
                                                          Resources.NeedCorrectExt,
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
      Logger.DebugFormat("Debug SendToAddressee - 1");
      var allRelatedDocuments = new List<Sungero.Content.IElectronicDocument>();
      
      foreach (var relationName in RelationTypes.GetAll().Select(r => r.Name))
      {
        allRelatedDocuments.AddRange(document.Relations.GetRelated(relationName).
                                     Where(d => d.HasVersions));
        allRelatedDocuments.AddRange(document.Relations.GetRelatedFrom(relationName).
                                     Where(d => d.HasVersions));
      }
      
      Logger.DebugFormat("Debug SendToAddressee: allRelatedDocuments = {0}", allRelatedDocuments.Count);
      
      if (allRelatedDocuments.Any())
      {
        Logger.DebugFormat("Debug SendToAddressee - 1-1");
        var dialogSelectDocument = Dialogs.CreateInputDialog(Resources.RelatedDocumentsForSending);
        Logger.DebugFormat("Debug SendToAddressee - 1-2");
        var applications = dialogSelectDocument.AddSelectMany(Resources.SelectDocuments, false, Sungero.Content.ElectronicDocuments.Null).
          From(allRelatedDocuments.Distinct().ToArray());
        Logger.DebugFormat("Debug SendToAddressee - 1-3");
        
        if (dialogSelectDocument.Show() == DialogButtons.Ok)
        {
          Logger.DebugFormat("Debug SendToAddressee - 1-4");
          document.DocsToSendGD.Clear();
          
          foreach (var relatedDoc in applications.Value)
          {
            var newRelatedDoc = document.DocsToSendGD.AddNew();
            newRelatedDoc.Document = relatedDoc;
          }
          
          if (document.DocsToSendGD.Any())
            document.Save();
          
          Logger.DebugFormat("Debug SendToAddressee - 1-5");
        }
        else
        {
          return null;
        }
      }
      
      var information = new List<string>();
      
      // Отправка адресатам по E-mail.
      var method = Sungero.Docflow.MailDeliveryMethods.GetAll(m => m.Name == Sungero.Docflow.MailDeliveryMethods.Resources.EmailMethod).FirstOrDefault();
      var errorsEmail = new List<string>();
      var emailAddressees = document.Addressees.Cast<IOutgoingLetterAddressees>().Where(x => Equals(x.DeliveryMethod, method) && string.IsNullOrEmpty(x.DocumentState));
      
      if (method != null && emailAddressees.Any())
      {
        // Проверки для отправки по Email.
        errorsEmail.AddRange(PublicFunctions.Module.CheckRequisitesForEmail(document, method));
        var settings = Functions.Module.Remote.GetTransmitterSettings();
        
        if (settings.MaxAttachmentFileSize.HasValue && !Functions.Module.Remote.CheckPackageSize(document, settings.MaxAttachmentFileSize.Value))
          errorsEmail.Add(GD.TransmitterModule.Resources.GeneratedArchiveExceedsMaxSizeFormat(settings.MaxAttachmentFileSize.Value));
        
        if (!errorsEmail.Any())
        {
          Logger.DebugFormat("Debug SendToAddressee - 2");
          var sendDocumentToAddresseesEMail = AsyncHandlers.SendDocumentToAddresseesEMail.Create();
          sendDocumentToAddresseesEMail.DocumentId = document.Id;
          sendDocumentToAddresseesEMail.SenderId = Sungero.Company.Employees.Current != null ? Sungero.Company.Employees.Current.Id : -1;
          sendDocumentToAddresseesEMail.DocumentsSetId = Guid.NewGuid().ToString();
          sendDocumentToAddresseesEMail.ExecuteAsync();
          
          foreach(var emailAddresse in emailAddressees)
          {
            emailAddresse.DocumentState = Resources.AwaitingDispatch;
            emailAddresse.AddresserGD = Users.Current;
          }
          
          if (document.State.Properties.Addressees.IsChanged)
            information.Add(Resources.DocumentWasSending);
        }
      }

      Logger.DebugFormat("Debug SendToAddressee - 3");
      
      // Проверки для отправки по МЭДО.
      var errorsMEDO = new List<string>();
      var addressesMedoCount = document.Addressees.Cast<IOutgoingLetterAddressees>()
        .Where(a => a.DeliveryMethod != null)
        .Where(a => a.DeliveryMethod.Sid == MEDO.PublicConstants.Module.MedoDeliveryMethod &&
               string.IsNullOrEmpty(a.DocumentState))
        .Count();
      Logger.DebugFormat("Debug SendToAddressee - 4");
      
      if (addressesMedoCount > 0)
      {
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
          return null;
        
        var responseDoc = document.InResponseTo;
        
        if (GovernmentSolution.IncomingLetters.As(responseDoc) != null)
        {
          var signedBy = GovernmentSolution.IncomingLetters.As(responseDoc).SignedBy;
          
          if (GovernmentSolution.IncomingLetters.As(responseDoc).SignedBy == null)
            errorsMEDO.Add(Resources.NeedEnterSignedByFormat(responseDoc.Name));
        }
        
        if (document.AddendumsPageCount == null)
          errorsMEDO.Add(Resources.FillInAddendumsPageCount);
      }
      
      Logger.DebugFormat("Debug SendToAddressee - 5");
      
      // Проверки для отправки в Directum RX.
      var errorsRX = PublicFunctions.Module.Remote.CheckRequisitesForSendRX(document);
      Logger.DebugFormat("Debug SendToAddressee - 6");
      
      // Если проверки для отправки не пройдены - не менять статус для адресатов.
      var addresses = document.Addressees.Cast<IOutgoingLetterAddressees>()
        .Where(a => a.DeliveryMethod != null)
        .Where(a => (a.DeliveryMethod.Sid == PublicConstants.Module.DeliveryMethod.DirectumRX && !errorsRX.Any() ||
                     a.DeliveryMethod.Sid == MEDO.PublicConstants.Module.MedoDeliveryMethod && !errorsMEDO.Any()) &&
               string.IsNullOrEmpty(a.DocumentState));
      Logger.DebugFormat("Debug SendToAddressee - 7");
      
      // Фиксация в истории отправки.
      var operation = new Enumeration(Constants.Module.SendAddressees);
      
      // TODO. Чтобы запись в истории отображалась корректно, название действия (переменная operation) необходимо локализовать
      // (создать ресурс в необходимом типе документа с названием Enum_Operation_<значение перечисления>. Например, Enum_Operation_SendAddressees)
      if (addresses.Any() ||
          document.Addressees.Any(x => Equals(x.DeliveryMethod, method) && (x as IOutgoingLetterAddressees).DocumentState != Resources.DeliveryState_Sent))
      {
        document.History.Write(operation, operation, document.Name);
        
        if (document.DocsToSendGD.Any())
        {
          foreach (var item in document.DocsToSendGD)
          {
            document.History.Write(operation, operation, item.Document.Name);
          }
        }
      }
      
      foreach (var addresse in addresses)
      {
        addresse.DocumentState = Resources.AwaitingDispatch;
        addresse.AddresserGD = Users.Current.IsSystem == true ? null : Users.Current;
      }
      
      Logger.DebugFormat("Debug SendToAddressee - 8");
      
      // Вызвать асинхронный обработчик для отправки.
      if (document.State.Properties.Addressees.IsChanged)
      {
        Logger.DebugFormat("Debug SendToAddressee - 9");
        
        if (!document.IsManyAddressees.Value)
          document.DocumentState = Resources.AwaitingDispatch;
        document.Save();
        Logger.DebugFormat("Debug SendToAddressee - 10");
        PublicFunctions.Module.StartSendingDocuments(document);
        Logger.DebugFormat("Debug SendToAddressee - 11");
        information.Add(Resources.DocumentWasSending);
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
      var transferErrors = new List<string>();
      
      if (CitizenRequests.PublicFunctions.OutgoingRequestLetter.Remote.IsTransfer(document))
      {
        transferErrors = CitizenRequests.PublicFunctions.Module.Remote.CheckRequisitesForInternalTransfer(document);
        var request = document.Request ?? document.Requests.Select(r => r.Request).FirstOrDefault();
        var relatedDocs = request.Relations
          .GetRelated().Union(request.Relations.GetRelatedFrom())
          .Where(d => d.HasVersions && !Equals(d, document));
        
        foreach (var relatedDoc in relatedDocs)
        {
          var newRelatedDoc = document.DocsToSendGD.AddNew();
          newRelatedDoc.Document = relatedDoc;
        }
      }
      else
      {
        // Выбрать связанные документы для отправки.
        var allRelatedDocuments = new List<Sungero.Content.IElectronicDocument>();
        
        foreach (var relationName in RelationTypes.GetAll().Select(r => r.Name))
        {
          allRelatedDocuments.AddRange(document.Relations.GetRelated(relationName).
                                       Where(d => d.HasVersions));
          allRelatedDocuments.AddRange(document.Relations.GetRelatedFrom(relationName).
                                       Where(d => d.HasVersions));
        }
        
        Logger.DebugFormat("Debug SendToAddressee: allRelatedDocuments = {0}", allRelatedDocuments.Count);
        
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
      var information = new List<string>();
      
      // Отправка адресатам по E-mail.
      var method = Sungero.Docflow.MailDeliveryMethods.GetAll(m => m.Name == Sungero.Docflow.MailDeliveryMethods.Resources.EmailMethod).FirstOrDefault();
      var errorsEmail = new List<string>();
      var emailAddressees = document.Addressees.Cast<IOutgoingRequestLetterAddressees>().Where(x => Equals(x.DeliveryMethod, method) &&                                                                                    string.IsNullOrEmpty(x.DocumentState));
      
      if (method != null && emailAddressees.Any())
      {
        // Проверки для отправки по Email.
        errorsEmail.AddRange(PublicFunctions.Module.CheckRequisitesForEmail(document, method));
        var settings = Functions.Module.Remote.GetTransmitterSettings();
        
        if (settings.MaxAttachmentFileSize.HasValue && !Functions.Module.Remote.CheckPackageSize(document, settings.MaxAttachmentFileSize.Value))
          errorsEmail.Add(GD.TransmitterModule.Resources.GeneratedArchiveExceedsMaxSizeFormat(settings.MaxAttachmentFileSize.Value));
        
        if (!errorsEmail.Any())
        {
          var sendDocumentToAddresseesEMail = AsyncHandlers.SendDocumentToAddresseesEMail.Create();
          sendDocumentToAddresseesEMail.DocumentId = document.Id;
          sendDocumentToAddresseesEMail.SenderId = Sungero.Company.Employees.Current != null ? Sungero.Company.Employees.Current.Id : -1;
          sendDocumentToAddresseesEMail.DocumentsSetId = Guid.NewGuid().ToString();
          sendDocumentToAddresseesEMail.ExecuteAsync();
          
          foreach(var emailAddresse in emailAddressees)
          {
            emailAddresse.DocumentState = Resources.AwaitingDispatch;
            emailAddresse.Addresser = Users.Current;
          }
          
          if (document.State.Properties.Addressees.IsChanged)
            information.Add(Resources.DocumentWasSending);
        }
      }
      
      // Проверки для отправки по МЭДО.
      var errorsMEDO = new List<string>();
      var addressesMedoCount = document.Addressees.Cast<IOutgoingRequestLetterAddressees>()
        .Where(a => a.DeliveryMethod != null &&
               a.DeliveryMethod.Sid == MEDO.PublicConstants.Module.MedoDeliveryMethod &&
               string.IsNullOrEmpty(a.DocumentState))
        .Count();
      
      if (addressesMedoCount > 0)
      {
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
          return null;
        
        var responseDoc = document.InResponseTo;
        
        if (GovernmentSolution.IncomingLetters.As(responseDoc) != null)
        {
          var signedBy = GovernmentSolution.IncomingLetters.As(responseDoc).SignedBy;
          
          if (GovernmentSolution.IncomingLetters.As(responseDoc).SignedBy == null)
            errorsMEDO.Add(Resources.NeedEnterSignedByFormat(responseDoc.Name));
        }
        
        if (document.AddendumsPageCount == null)
          errorsMEDO.Add(Resources.FillInAddendumsPageCount);
      }
      
      // Если проверки для отправки не пройдены - не менять статус для адресатов.
      var addresses = document.Addressees.Cast<IOutgoingRequestLetterAddressees>()
        .Where(a => a.DeliveryMethod != null)
        .Where(a => (a.DeliveryMethod.Sid == MEDO.PublicConstants.Module.MedoDeliveryMethod && !errorsMEDO.Any() ||
                     a.DeliveryMethod.Sid == CitizenRequests.PublicFunctions.Module.Remote.GetDirectumRXDeliveryMethodSid() && !transferErrors.Any()) &&
               string.IsNullOrEmpty(a.DocumentState));
      
      // Фиксация в истории отправки.
      var operation = new Enumeration(Constants.Module.SendAddressees);
      
      // TODO. Чтобы запись в истории отображалась корректно, название действия (переменная operation) необходимо локализовать
      // (создать ресурс в необходимом типе документа с названием Enum_Operation_<значение перечисления>. Например, Enum_Operation_SendAddressees)
      if (addresses.Any() ||
          document.Addressees.Any(x => Equals(x.DeliveryMethod, method) && (x as IOutgoingRequestLetterAddressees).DocumentState != Resources.DeliveryState_Sent))
      {
        document.History.Write(operation, operation, document.Name);
        
        if (document.DocsToSendGD.Any())
        {
          foreach (var item in document.DocsToSendGD)
          {
            document.History.Write(operation, operation, item.Document.Name);
          }
        }
      }
      
      foreach (var addresse in addresses)
      {
        addresse.DocumentState = Resources.AwaitingDispatch;
        addresse.Addresser = Users.Current.IsSystem == true ? null : Users.Current;
      }
      
      // Вызвать асинхронный обработчик для отправки.
      if (document.State.Properties.Addressees.IsChanged)
      {
        if (!document.IsManyAddressees.Value)
          document.DocumentState = Resources.AwaitingDispatch;
        
        document.Save();
        PublicFunctions.Module.StartSendingDocuments(document);
        information.Add(Resources.DocumentWasSending);
      }
      
      var addresseesWithoutDeliveryMethod = document.Addressees.Where(a => a.DeliveryMethod == null);
      
      foreach (var addressee in addresseesWithoutDeliveryMethod)
        transferErrors.Add(GD.TransmitterModule.Resources.CorrespondentDeliveryMethodIsEmptyFormat(addressee.Correspondent.Name));
      
      return Structures.Module.SendToAddresseeResult.Create(information, transferErrors, errorsMEDO, errorsEmail);
    }
  }
}