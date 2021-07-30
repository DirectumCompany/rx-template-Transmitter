using System;
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
      var information = new List<string>();
      
      // Выбрать связанные документы для отправки.
      Logger.DebugFormat("Debug SendToAddressee - 1");
      var allRelatedDocuments = new List<Sungero.Content.IElectronicDocument>();
      foreach (var relationName in RelationTypes.GetAll().Select(r => r.Name))
      {
        allRelatedDocuments.AddRange(document.Relations.GetRelated(relationName).
                                     Where(d => Sungero.Content.ElectronicDocuments.Is(d) && d.HasVersions).Select(d => Sungero.Content.ElectronicDocuments.As(d)).ToList());
        allRelatedDocuments.AddRange(document.Relations.GetRelatedFrom(relationName).
                                     Where(d => Sungero.Content.ElectronicDocuments.Is(d) && d.HasVersions).Select(d => Sungero.Content.ElectronicDocuments.As(d)).ToList());
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
      
      // Отправка адресатам по E-mail.
      var method = Sungero.Docflow.MailDeliveryMethods.GetAll(m => m.Name == Sungero.Docflow.MailDeliveryMethods.Resources.EmailMethod).FirstOrDefault();
      
      // Добавление возможности отправки копии письма на указанный адрес электронной почты.
      // Проверка, заполнено ли хотя бы у одного адресата поле "Направить копию".
      /*var isExistsCopyTo = _obj.Addressees.Cast<IOutgoingLetterAddressees>().
        Any(x => !string.IsNullOrEmpty(x.CopyTo) && x.CopyStatus != OutgoingLetters.Resources.DeliveryState_Sent);
      
      if (method != null && (_obj.Addressees.Cast<IOutgoingLetterAddressees>().Any(x => x.DeliveryMethod == method &&
                                                                                                x.DocumentState != OutgoingLetters.Resources.DeliveryState_Sent) ||
                             isExistsCopyTo))*/
      
      var errorsEmail = new List<string>();
      if (method != null &&
          (document.Addressees.Cast<IOutgoingLetterAddressees>().Any(x => Equals(x.DeliveryMethod, method) &&
                                                                     x.DocumentState != Resources.DeliveryState_Sent)))
      {
        Logger.DebugFormat("Debug SendToAddressee - 2");
        // Проверки для отправки по Email.
        errorsEmail = PublicFunctions.Module.Remote.CheckRequisitesForEmail(document);
        var sendDocumentToAddresseesEMail = AsyncHandlers.SendDocumentToAddresseesEMail.Create();
        sendDocumentToAddresseesEMail.DocumentId = document.Id;
        sendDocumentToAddresseesEMail.Sender = Sungero.Company.Employees.Current != null ? Sungero.Company.Employees.Current.Name : string.Empty;
        sendDocumentToAddresseesEMail.ExecuteAsync();
        
        // Проставление статуса по аналогии с исх. по обращению.
        var addressesEmail = document.Addressees.Cast<IOutgoingLetterAddressees>()
          .Where(x => Equals(x.DeliveryMethod, method) && x.DocumentState != Resources.DeliveryState_Sent && !errorsEmail.Any());
        
        foreach(var addresseEmail in addressesEmail)
        {
          addresseEmail.DocumentState = Resources.AwaitingDispatch;
          addresseEmail.AddresserGD = Users.Current;
        }
        
        // Добавление возможности отправки копии письма на указанный адрес электронной почты.
        /*var copyAddresses = document.Addressees.Cast<IOutgoingLetterAddressees>()
          .Where(x => !string.IsNullOrWhiteSpace(x.CopyTo) && x.CopyStatus != OutgoingLetters.Resources.DeliveryState_Sent);
        foreach (var item in copyAddresses)
          item.CopyStatus = OutgoingLetters.Resources.AwaitingDispatch;*/
        if (document.State.Properties.Addressees.IsChanged)
          information.Add(Resources.DocumentWasSending);
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
        var needCancle = PublicFunctions.Module.CheckExtForMedo(document);
        if (needCancle)
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
      // Добавление возможности отправки копии письма на указанный адрес электронной почты.
      //var operationCopy = new Enumeration(Constants.Module.SendCopyTo);
      
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
      
      // Добавление возможности отправки копии письма на указанный адрес электронной почты.
      /*foreach (var addresse in document.Addressees.Cast<IOutgoingLetterAddressees>().Where(x => !string.IsNullOrWhiteSpace(x.CopyTo) && x.CopyStatus != Resources.DeliveryState_Sent))
      {
        document.History.Write(operationCopy, operationCopy, OutgoingLetters.Resources.AddressAddMailFormat(addresse.CopyTo, document.Name));
        
        if (document.DocsToSendGD.Any())
        {
          foreach (var item in document.DocsToSendGD)
          {
            document.History.Write(operationCopy, operationCopy, OutgoingLetters.Resources.AddressAddMailFormat(addresse.CopyTo, item.Document.Name));
          }
        }
      }*/
      
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
        PublicFunctions.Module.Remote.SendingDocumentAsyncHandlers(document);
        Logger.DebugFormat("Debug SendToAddressee - 11");
        information.Add(Resources.DocumentWasSending);
      }
      
      return Structures.Module.SendToAddresseeResult.Create(information, errorsRX, errorsMEDO, errorsEmail);
    }

  }
}