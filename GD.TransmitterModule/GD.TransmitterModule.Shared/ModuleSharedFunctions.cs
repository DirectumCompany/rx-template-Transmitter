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
          .Where(a => Equals(a.DeliveryMethod, method) && string.IsNullOrEmpty(a.DocumentState));
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
          .Where(a => Equals(a.DeliveryMethod, method) && string.IsNullOrEmpty(a.DocumentState));
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
    [Public]
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