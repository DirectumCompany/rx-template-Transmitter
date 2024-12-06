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
    public virtual void SendDocumentToAddresseesInternalMail(GD.TransmitterModule.Server.AsyncHandlerInvokeArgs.SendDocumentToAddresseesInternalMailInvokeArgs args)
    {
      var letter = Sungero.Docflow.OfficialDocuments.Get(args.DocumentID);

      if (letter == null)
        return;
      
      try
      {
        var request = args.RequestId != 0 ? GD.CitizenRequests.Requests.Get(args.RequestId) : GD.CitizenRequests.Requests.Null;
        var relatedDocIDs = args.RelationDocumentIDs.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(long.Parse).ToList();
        var relatedDocs = Sungero.Content.ElectronicDocuments.GetAll(d => relatedDocIDs.Contains(d.Id));
        
        Logger.Debug("SendDocumentToAddressees: start SendDocumentToAddressees.");
        
        if (Sungero.Docflow.OutgoingDocumentBases.Is(letter))
          Functions.Module.SendDocumentToAddresseesInternalMail(GovernmentSolution.OutgoingDocumentBases.As(letter), relatedDocs, args.IsRequestTransfer, request);
        
        Logger.Debug("SendDocumentToAddressees: end SendDocumentToAddressees.");
      }
      catch (Exception ex)
      {
        Logger.Error("SendDocumentToAddressees: document is locked.", ex);
        args.Retry = true;
        return;
      }
    }
    
    public virtual void SendDocumentToAddresseesMedo(GD.TransmitterModule.Server.AsyncHandlerInvokeArgs.SendDocumentToAddresseesMedoInvokeArgs args)
    {
      var letter = Sungero.Docflow.OfficialDocuments.Get(args.DocumentID);
      
      if (letter == null)
        return;
      
      var sender = args.SenderId != null ? Users.GetAll(x => x.Id == args.SenderId).FirstOrDefault() : null;
      try
      {
        if (!Locks.TryLock(letter))
        {
          args.Retry = true;
          return;
        }
        
        var relatedDocs = Enumerable.Empty<Sungero.Content.IElectronicDocument>().AsQueryable();
        var relatedDocIDs = new List<long>();
        
        if (!string.IsNullOrEmpty(args.RelationDocumentIDs))
          relatedDocIDs = args.RelationDocumentIDs.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(long.Parse).ToList();
        
        relatedDocs = Sungero.Content.ElectronicDocuments.GetAll(d => relatedDocIDs.Contains(d.Id));
        
        Logger.Debug("SendDocumentToAddresseesMedo: start SendDocumentToAddresseesMedo.");
        
        if (Sungero.Docflow.OutgoingDocumentBases.Is(letter))
          Functions.Module.SendDocumentToAddresseesMedo(GovernmentSolution.OutgoingDocumentBases.As(letter), relatedDocs, sender);
        
        Logger.Debug("SendDocumentToAddresseesMedo: end SendDocumentToAddresseesMedo.");
      }
      catch (Exception ex)
      {
        Logger.Error("SendDocumentToAddresseesMedo: Error occured : {0}", ex);
        args.Retry = true;
        return;
      }
      finally
      {
        if (Locks.GetLockInfo(letter).IsLockedByMe)
          Locks.Unlock(letter);
      }
    }

    /// <summary>
    /// Отправка документов адресатам по e-mail.
    /// </summary>
    /// <param name="Sender">Отправитель.</param>
    /// <param name="DocumentID">ИД Документа.</param>
    public virtual void SendDocumentToAddresseesEMail(GD.TransmitterModule.Server.AsyncHandlerInvokeArgs.SendDocumentToAddresseesEMailInvokeArgs args)
    {
      var method = Sungero.Docflow.MailDeliveryMethods.GetAll(m => m.Name == Sungero.Docflow.MailDeliveryMethods.Resources.EmailMethod).FirstOrDefault();
      
      if (method == null)
      {
        Logger.Error("Debug SendDocumentToAddresseesEMail: Error = Не найден способ доставки по e-mail.");
        return;
      }
      
      var letter = Sungero.Docflow.OutgoingDocumentBases.Get(args.DocumentId);
      
      if (letter == null)
      {
        Logger.Error("Debug SendDocumentToAddresseesEMail: Error = Исходящее письмо не найдено.");
        return;
      }
      
      try
      {
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
            Functions.Module.CreateEmailRegisterRecord(letter, item, args.Sender, copyTo);
          }
          if (item.DeliveryMethod != null && item.DeliveryMethod.Equals(method) && isStateNotSent)
          {
            Functions.Module.CreateEmailRegisterRecord(letter, item, args.Sender, string.Empty);
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
            Functions.Module.CreateEmailRegisterRecord(letter, item, args.SenderId, args.DocumentsSetId);
          }
        }
      }
      catch (Exception ex)
      {
        Logger.ErrorFormat("Debug SendDocumentToAddresseesEMail: Error = " + ex.Message);
      }
    }

  }
}