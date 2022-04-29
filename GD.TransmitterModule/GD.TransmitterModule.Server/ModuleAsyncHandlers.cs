using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;
using GD.GovernmentSolution;
using GD.CitizenRequests;

namespace GD.TransmitterModule.Server
{
  public class ModuleAsyncHandlers
  {

    public virtual void HandleErrorInDocumentProcessingTask(GD.TransmitterModule.Server.AsyncHandlerInvokeArgs.HandleErrorInDocumentProcessingTaskInvokeArgs args)
    {
      var item = InternalMailRegisters.GetAll(i => i.Id == args.InternalMailRegisterId).FirstOrDefault();
      
      if (item == null)
        return;
      
      if (Locks.GetLockInfo(item).IsLocked)
      {
        args.Retry = true;
        return;
      }
      
      var document = item.LeadingDocument;
      
      if (Locks.GetLockInfo(document).IsLocked)
      {
        args.Retry = true;
        return;
      }
      
      var correspondent = item.Correspondent;
      
      try
      {
        
        item.Status = GD.TransmitterModule.InternalMailRegister.Status.Error;
        item.ErrorInfo = args.ErrorMessage.Substring(0, args.ErrorMessage.Length < 1000 ? args.ErrorMessage.Length : 1000);
        item.Save();
        
        Functions.Module.SendNoticeToResponsible(item);
        var error = GD.TransmitterModule.Resources.DeliveryState_Error;
        
        if (OutgoingLetters.Is(document))
        {
          var outgoingLetter = OutgoingLetters.As(document);
          
          var addressee = (IOutgoingLetterAddressees)outgoingLetter.Addressees.Where(x => Equals(x.Correspondent, correspondent) && x.DeliveryMethod != null).FirstOrDefault();
          if (addressee != null)
          {
            addressee.DocumentState = error;
            addressee.StateInfo = error;
          }
          if (outgoingLetter.IsManyAddressees == false)
          {
            outgoingLetter.DocumentState = error;
            outgoingLetter.StateInfo = error;
          }
          
          outgoingLetter.Save();
        }
        else if (OutgoingRequestLetters.Is(document))
        {
          var outgoingRequestLetter = OutgoingRequestLetters.As(document);
          
          var addressee = (IOutgoingRequestLetterAddressees)outgoingRequestLetter.Addressees.Where(x => Equals(x.Correspondent, correspondent) && x.DeliveryMethod != null).FirstOrDefault();
          if (addressee != null)
          {
            addressee.DocumentState = error;
            addressee.StateInfo = error;
          }
          if (outgoingRequestLetter.IsManyAddressees == false)
          {
            outgoingRequestLetter.DocumentState = error;
            outgoingRequestLetter.StateInfo = error;
          }
          
          outgoingRequestLetter.Save();
        }
      }
      catch (Exception ex)
      {
        Logger.ErrorFormat("An error occured while update state. RegisterItem = {0}", ex, item.Id);
      }
    }
    
    /// <summary>
    /// Отправка документов адресатам.
    /// </summary>
    /// <param name="DocumentID">ИД Документа.</param>
    /// <param name="RelationDocumentIDs">Список связанных документов.</param>
    public virtual void SendDocumentToAddressees(GD.TransmitterModule.Server.AsyncHandlerInvokeArgs.SendDocumentToAddresseesInvokeArgs args)
    {
      var letter = Sungero.Docflow.OfficialDocuments.GetAll(d => d.Id == args.DocumentID).FirstOrDefault();
      if (letter == null)
        return;
      var relatedDocIDs = args.RelationDocumentIDs.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
      var intList = new List<int>();
      foreach (var ids in relatedDocIDs)
      {
        var intId = 0;
        if (int.TryParse(ids, out intId))
          intList.Add(intId);
      }
      var relatedDocs = Sungero.Content.ElectronicDocuments.GetAll(d => intList.Contains(d.Id));
      
      if (Locks.GetLockInfo(letter).IsLocked)
      {
        args.Retry = true;
        return;
      }
      try
      {
        Logger.Debug("SendDocumentToAddressees: start SendDocumentToAddressees.");
        if (Sungero.Docflow.OutgoingDocumentBases.Is(letter))
          Functions.Module.SendDocumentToAddressees(Sungero.Docflow.OutgoingDocumentBases.As(letter), relatedDocs);
        Logger.Debug("SendDocumentToAddressees: end SendDocumentToAddressees.");
      }
      catch (Exception ex)
      {
        Logger.Error("SendDocumentToAddressees: document is locked.", ex);
        args.Retry = true;
        return;
      }
    }

    /// <summary>
    /// Отправка документов адресатам по e-mail.
    /// </summary>
    /// <param name="Sender">Отправитель.</param>
    /// <param name="DocumentID">ИД Документа.</param>
    public virtual void SendDocumentToAddresseesEMail(GD.TransmitterModule.Server.AsyncHandlerInvokeArgs.SendDocumentToAddresseesEMailInvokeArgs args)
    {
      try
      {
        var method = Sungero.Docflow.MailDeliveryMethods.GetAll(m => m.Name == Sungero.Docflow.MailDeliveryMethods.Resources.EmailMethod).FirstOrDefault();
        
        if (method == null)
        {
          Logger.Error("Debug SendDocumentToAddresseesEMail: Error = Не найден способ доставки по e-mail.");
          return;
        }
        
        var letter = Sungero.Docflow.OutgoingDocumentBases.GetAll(x => x.Id == args.DocumentId).FirstOrDefault();
        
        if (letter == null)
        {
          Logger.Error("Debug SendDocumentToAddresseesEMail: Error = Исходящее письмо не найдено.");
          return;
        }
        
        Logger.DebugFormat("Debug SendDocumentToAddresseesEMail: FileName - " + string.Format(@"{0}.{1}", letter.Name, "pdf"));
        
        /*var addressees = letter.Addressees.Where(x => x.DeliveryMethod != null && Equals(x.DeliveryMethod.Name,method.Name) ||
                                                 OutgoingLetters.Is(x.RootEntity) && !string.IsNullOrEmpty((x as IOutgoingLetterAddressees).CopyTo) ||
                                                 OutgoingRequestLetters.Is(x.RootEntity) && !string.IsNullOrEmpty((x as IOutgoingRequestLetterAddressees).CopyTo));*/
        var addressees = letter.Addressees.Where(x => x.DeliveryMethod != null && Equals(x.DeliveryMethod.Name,method.Name));
        
        Logger.DebugFormat("Debug SendDocumentToAddresseesEMail: Count = " + addressees.Count());
        
        // Добавление возможности отправки копии письма на указанный адрес электронной почты.
        /*foreach (var item in addressees)
        {
          var copyTo = string.Empty;
          var isStateNotSent = false;
          var isCopyNotSent = false;
          if (OutgoingLetters.Is(letter))
          {
            copyTo = (item as IOutgoingLetterAddressees).CopyTo;
            isStateNotSent = (item as IOutgoingLetterAddressees).DocumentState != Resources.DeliveryState_Sent;
            isCopyNotSent = (item as IOutgoingLetterAddressees).CopyStatus != Resources.DeliveryState_Sent;
          }
          else if (OutgoingRequestLetters.Is(letter))
          {
            copyTo = (item as IOutgoingRequestLetterAddressees).CopyTo;
            isStateNotSent = (item as IOutgoingRequestLetterAddressees).DocumentState != Resources.DeliveryState_Sent;
            isCopyNotSent = (item as IOutgoingRequestLetterAddressees).CopyStatus != Resources.DeliveryState_Sent;
          }
          // Создание сообщения для копии.
          if (!string.IsNullOrEmpty(copyTo) && isCopyNotSent)
          {
            Functions.Module.CreateMailRegisterItem(letter, item, args.Sender, copyTo);
          }
          if (item.DeliveryMethod != null && item.DeliveryMethod.Equals(method) && isStateNotSent)
          {
            Functions.Module.CreateMailRegisterItem(letter, item, args.Sender, string.Empty);
          }
        }*/
        
        foreach (var item in addressees)
        {
          var isStateNotSent = false;
          if (OutgoingLetters.Is(letter))
          {
            isStateNotSent = (item as IOutgoingLetterAddressees).DocumentState != Resources.DeliveryState_Sent;
          }
          else if (OutgoingRequestLetters.Is(letter))
          {
            isStateNotSent = (item as IOutgoingRequestLetterAddressees).DocumentState != Resources.DeliveryState_Sent;
          }
          // Создание сообщения для копии.
          if (item.DeliveryMethod != null && item.DeliveryMethod.Equals(method) && isStateNotSent)
          {
            Functions.Module.CreateMailRegisterItem(letter, item, args.SenderId, args.DocumentsSetId);
          }
        }
      }
      catch (Exception ex)
      {
        Logger.DebugFormat("Debug SendDocumentToAddresseesEMail: Error = " + ex.Message);
      }
    }

  }
}