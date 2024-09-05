using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;
using GD.GovernmentSolution;
using GD.CitizenRequests;

namespace GD.TransmitterModule.Shared
{
  public class ModuleFunctions
  {
    /// <summary>
    /// Проверить реквизиты для отправки по Email.
    /// </summary>
    /// <param name="document">Основной документ для отправки</param>
    /// <param name="method">Способ доставки.</param>
    /// <returns>Список ошибок.</returns>
    [Public]
    public List<string> CheckRequisitesForEmail(Sungero.Docflow.IOfficialDocument document, Sungero.Docflow.IMailDeliveryMethod method)
    {
      Logger.DebugFormat("Debug CheckRequisitesForEmail - start");
      var errors = new List<string>();
      
      if (OutgoingLetters.Is(document))
      {
        var addresses = OutgoingLetters.As(document).Addressees.Cast<IOutgoingLetterAddressees>()
          .Where(a => Equals(a.DeliveryMethod, method) && string.IsNullOrEmpty(a.DocumentState) && string.IsNullOrEmpty(a.EmailGD));
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
      else if (OutgoingRequestLetters.Is(document))
      {
        var addresses = OutgoingRequestLetters.As(document).Addressees.Cast<IOutgoingRequestLetterAddressees>()
          .Where(a => Equals(a.DeliveryMethod, method) && string.IsNullOrEmpty(a.DocumentState) && string.IsNullOrEmpty(a.EmailGD));
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
    /// Вызвать обработчик для отправки исходящего письма адресатам.
    /// </summary>
    /// <param name="document">Основной документ для отправки.</param>
    [Public]
    public void StartInternalSendingDocuments(Sungero.Docflow.IOfficialDocument document)
    {
      Logger.DebugFormat("StartSendingDocuments. Start function for document Id = {0}.", document.Id);
      
      var relatedDocumentsIds = string.Empty;
      var outgoingRequestLetter = OutgoingRequestLetters.As(document);
      var isTransfer = false;
      
      if (OutgoingLetters.Is(document))
        relatedDocumentsIds = string.Join(",", OutgoingLetters.As(document).DocsToSendGD.Where(d => d.Document != null).Select(d => d.Document.Id).ToList());
      else if (outgoingRequestLetter != null)
      {
        relatedDocumentsIds = string.Join(",", outgoingRequestLetter.DocsToSendGD.Where(d => d.Document != null).Select(d => d.Document.Id).ToList());
        isTransfer = CitizenRequests.PublicFunctions.OutgoingRequestLetter.IsTransfer(outgoingRequestLetter);
      }
      
      var asyncSendingHandler = AsyncHandlers.SendDocumentToAddresseesInternalMail.Create();
      asyncSendingHandler.DocumentID = document.Id;
      asyncSendingHandler.RelationDocumentIDs = relatedDocumentsIds;
      asyncSendingHandler.IsRequestTransfer = isTransfer; 
      
      if (isTransfer)
      {
        var request = outgoingRequestLetter.Request ?? outgoingRequestLetter.Requests.FirstOrDefault().Request;
        asyncSendingHandler.RequestId = request.Id;
      }
      asyncSendingHandler.ExecuteAsync();
      
      Logger.DebugFormat("StartSendingDocuments. End function for document Id = {0}.", document.Id);
    }
    
    /// <summary>
    /// Стартовать АО для отправки документа по Email.
    /// </summary>
    /// <param name="document">Документ.</param>
    [Public]
    public virtual void StartSendingDocumentToAddresseesEMail(Sungero.Docflow.IOutgoingDocumentBase document)
    {
      var sendDocumentToAddresseesEMail = AsyncHandlers.SendDocumentToAddresseesEMail.Create();
      sendDocumentToAddresseesEMail.DocumentId = document.Id;
      sendDocumentToAddresseesEMail.SenderId = Sungero.Company.Employees.Current != null ? Sungero.Company.Employees.Current.Id : -1;
      sendDocumentToAddresseesEMail.DocumentsSetId = Guid.NewGuid().ToString();
      sendDocumentToAddresseesEMail.ExecuteAsync();
    }
    
    /// <summary>
    /// Запустить АО для отправки докумнета по МЭДО.
    /// </summary>
    /// <param name="document">Документ.</param>
    [Public]
    public virtual void StartStartSendingDocumentToAddresseesMedo(Sungero.Docflow.IOutgoingDocumentBase document, IUser sender)
    {
      var relatedDocuments = OutgoingLetters.Is(document) ?
        OutgoingLetters.As(document).DocsToSendGD.Where(d => d.Document != null &&
                                                        d.Document.AssociatedApplication.Extension.Length >= 3 && 
                                                        d.Document.AssociatedApplication.Extension.Length <= 4).Select(d => d.Document.Id).ToList() :
        OutgoingRequestLetters.As(document).DocsToSendGD.Where(d => d.Document != null && 
                                                        d.Document.AssociatedApplication.Extension.Length >= 3 &&
                                                        d.Document.AssociatedApplication.Extension.Length <= 4).Select(d => d.Document.Id).ToList();
      var relatedDocumentsIds = string.Join(",", relatedDocuments);
      var asyncSendingHandler = AsyncHandlers.SendDocumentToAddresseesMedo.Create();
      asyncSendingHandler.DocumentID = document.Id;
      asyncSendingHandler.RelationDocumentIDs = relatedDocumentsIds;
      asyncSendingHandler.SenderId = sender == null ? 0 : sender.Id;
      asyncSendingHandler.ExecuteAsync();
    }
  }
}