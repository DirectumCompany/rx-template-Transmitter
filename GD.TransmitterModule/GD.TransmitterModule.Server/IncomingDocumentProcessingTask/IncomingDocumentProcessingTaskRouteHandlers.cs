using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;
using Sungero.Workflow;
using GD.TransmitterModule.IncomingDocumentProcessingTask;
using GD.GovernmentSolution;
using GD.CitizenRequests;

namespace GD.TransmitterModule.Server
{
  partial class IncomingDocumentProcessingTaskRouteHandlers
  {

    #region 5 - Задание на доработку.
    public virtual void StartBlock5(GD.TransmitterModule.Server.IncomingDocumentProcessingReworkAssignmentArguments e)
    {
      var document = _obj.ReasonDoc;
      e.Block.IsParallel = true;
      IRecipient sender = null;
      if (OutgoingLetters.Is(_obj.ReasonDoc))
      {
        var addresses = OutgoingLetters.As(_obj.ReasonDoc).Addressees.Cast<IOutgoingLetterAddressees>().
          Where(a => Sungero.Parties.Companies.Equals(a.Correspondent, _obj.ResultDoc.BusinessUnit.Company)).FirstOrDefault();
        if (addresses != null)
          sender = addresses.AddresserGD;
      }
      else if (OutgoingRequestLetters.Is(_obj.ReasonDoc))
      {
        var addresses = OutgoingRequestLetters.As(_obj.ReasonDoc).Addressees.Cast<IOutgoingRequestLetterAddressees>().
          Where(a => Sungero.Parties.Companies.Equals(a.Correspondent, _obj.ResultDoc.BusinessUnit.Company)).FirstOrDefault();
        if (addresses != null)
          sender = addresses.Addresser;
      }
      // Добавление возможности перенаправления входящих писем с помощью реализованного механизма.
      /*else if (IncomingLetters.Is(_obj.ReasonDoc))
      {
        var addresses = IncomingLetters.As(_obj.ReasonDoc).FanSendingGD
          .Where(a => Sungero.Parties.Companies.Equals(a.Correspondent, _obj.ResultDoc.BusinessUnit.Company)).FirstOrDefault();
        if (addresses != null)
          sender = addresses.Sender;
      }*/
      if (sender == null || sender.IsSystem == true)
        sender = PublicFunctions.Module.Remote.GetRegistrarForBusinessUnit(document.BusinessUnit, document.DocumentKind);
      e.Block.Performers.Add(sender);
    }
    
    public virtual void StartAssignment5(GD.TransmitterModule.IIncomingDocumentProcessingReworkAssignment assignment, GD.TransmitterModule.Server.IncomingDocumentProcessingReworkAssignmentArguments e)
    {
      var subject = Sungero.Docflow.PublicFunctions.Module.TrimSpecialSymbols(IncomingDocumentProcessingReworkAssignments.Resources.AssignmentSubject, _obj.ReasonDoc.Name);
      assignment.Subject = subject.Substring(0, subject.Length > 250 ? 250 : subject.Length);
    }
    
    public virtual void CompleteAssignment5(GD.TransmitterModule.IIncomingDocumentProcessingReworkAssignment assignment, GD.TransmitterModule.Server.IncomingDocumentProcessingReworkAssignmentArguments e)
    {
      var result = assignment.Result;
      // Завершить другие задания.
      var reworkDocumentAssignments = IncomingDocumentProcessingReworkAssignments.GetAll(j => Equals(j.Task, assignment.Task) &&
                                                                                         j.Status == Sungero.Workflow.Task.Status.InProcess);
      foreach (var reworkDocumentAssignment in reworkDocumentAssignments)
      {
        reworkDocumentAssignment.ActiveText = IncomingDocumentProcessingTasks.Resources.RegistrationCompleteOtherUserFormat(assignment.Performer.Name);
        reworkDocumentAssignment.Complete(result);
      }
      
      if (result == GD.TransmitterModule.IncomingDocumentProcessingReworkAssignment.Result.Corrected)
       Functions.Module.ChangeDocumentStateInfoInRegister(_obj.GeneratedFrom, Resources.DeliveryState_Sent, null);
    }
    #endregion

    #region 4 - Задание на регистрацию.
    public virtual void StartBlock4(GD.TransmitterModule.Server.IncomingDocumentProcessingRegistrationAssignmentArguments e)
    {
      var docKindIncLetter = Sungero.Docflow.DocumentKinds.GetAll(x => x.DocumentType.DocumentTypeGuid.ToLower() == PublicConstants.Module.IncLetterKind.ToLower()
                                                                  && x.IsDefault == true).FirstOrDefault();
      // Определить регистратора и выдать ему права
      e.Block.IsParallel = true;
      var registrator = _obj.Registrar;
      if (registrator == null)
        registrator = PublicFunctions.Module.Remote.GetRegistrarForBusinessUnit(_obj.ToBusinessUnit, docKindIncLetter);
      e.Block.Performers.Add(registrator);

    }
    
    public virtual void StartAssignment4(GD.TransmitterModule.IIncomingDocumentProcessingRegistrationAssignment assignment, GD.TransmitterModule.Server.IncomingDocumentProcessingRegistrationAssignmentArguments e)
    {
      var subject = Sungero.Docflow.PublicFunctions.Module.TrimSpecialSymbols(IncomingDocumentProcessingRegistrationAssignments.Resources.AssignmentSubject, _obj.ResultDoc.Name);
      assignment.Subject = subject.Substring(0, subject.Length > 250 ? 250 : subject.Length);
    }
    
    public virtual void CompleteAssignment4(GD.TransmitterModule.IIncomingDocumentProcessingRegistrationAssignment assignment, GD.TransmitterModule.Server.IncomingDocumentProcessingRegistrationAssignmentArguments e)
    {
      var result = assignment.Result;
      // Завершить другие задания.
      var registerDocumentAssignments = IncomingDocumentProcessingRegistrationAssignments.GetAll(j => Tasks.Equals(j.Task, assignment.Task) &&
                                                                                                 j.Status == Sungero.Workflow.Task.Status.InProcess);
      foreach (var registerDocumentAssignment in registerDocumentAssignments)
      {
        registerDocumentAssignment.ActiveText = IncomingDocumentProcessingTasks.Resources.RegistrationCompleteOtherUserFormat(assignment.Performer.Name);
        registerDocumentAssignment.Complete(result);
      }
      
      if (result == GD.TransmitterModule.IncomingDocumentProcessingRegistrationAssignment.Result.Register)
        Functions.Module.ChangeDocumentStateInfoInRegister(_obj.GeneratedFrom, Resources.DeliveryState_Registered,
                                                           IncomingDocumentProcessingTasks.Resources.RegistrationStringFormat(_obj.ResultDoc.RegistrationNumber,
                                                                                                                              _obj.ResultDoc.RegistrationDate.Value.ToShortDateString()));
      else if (result == GD.TransmitterModule.IncomingDocumentProcessingRegistrationAssignment.Result.Rework)
        Functions.Module.ChangeDocumentStateInfoInRegister(_obj.GeneratedFrom, Resources.DeliveryState_Return, assignment.ActiveText);
      
      if (result == GD.TransmitterModule.IncomingDocumentProcessingRegistrationAssignment.Result.RedirectToBusinessUnit)
      {
        _obj.ToBusinessUnitBefore = assignment.ToBusinessUnitBefore;
        _obj.ToBusinessUnit = assignment.ToBusinessUnit;
        _obj.ToCounterparty = assignment.ToCounterparty;
        
        if (_obj.ReasonDoc != null)
        {
          Functions.Module.ChangeDocumentStateInfoInRegister(_obj.GeneratedFrom, Resources.DeliveryState_RedirectedTo, Resources.DeliveryState_RedirectedTo);
          
          var newItem = InternalMailRegisters.Create();
          newItem.LeadingDocument = _obj.ReasonDoc;
          newItem.Correspondent = assignment.ToCounterparty;
          foreach (var row in _obj.GeneratedFrom.RelatedDocuments)
            newItem.RelatedDocuments.AddNew().Document = row.Document;
          
          newItem.Status = GD.TransmitterModule.InternalMailRegister.Status.Complete;
          newItem.TaskId = _obj.GeneratedFrom.TaskId;
          newItem.SyncStateInDocument = GD.TransmitterModule.InternalMailRegister.SyncStateInDocument.ToProcess;
          newItem.IsRedirect = true;
          newItem.Save();
          
          _obj.GeneratedFrom = newItem;
        }
      }
      if (result == GD.TransmitterModule.IncomingDocumentProcessingRegistrationAssignment.Result.RedirectToDepartment)
      {
        _obj.Registrar = assignment.Registrar;
      }
    }
    #endregion

    #region 3 - Создание/синхронизация входящего документа.
    public virtual void Script3Execute()
    {
      try
      {
        var reasonDoc = _obj.ReasonDoc;
        var reasonDocSubject = string.Empty;
        if (OutgoingLetters.Is(reasonDoc))
          reasonDocSubject = OutgoingLetters.As(reasonDoc).Subject;
        if (OutgoingRequestLetters.Is(reasonDoc))
          reasonDocSubject = OutgoingRequestLetters.As(reasonDoc).Subject;
        
        if (string.IsNullOrEmpty(reasonDocSubject) || string.IsNullOrWhiteSpace(reasonDocSubject))
          reasonDocSubject = reasonDoc.Subject;
        
        var document = IncomingLetters.As(_obj.ResultDoc);
        var businessUnit = _obj.ToBusinessUnit;
        // Создать/обновить входящее письмо
        if (document == null)
        {
          // Добавление возможности перенаправления входящих писем с помощью реализованного механизма.
          /*if (IncomingLetters.Is(reasonDoc))
        {
          document = IncomingLetters.Copy(IncomingLetters.As(reasonDoc));
          document.RegAGOData = string.Format("{0} {1} {2}", reasonDoc.RegistrationNumber, Sungero.Docflow.OfficialDocuments.Resources.DateFrom, reasonDoc.RegistrationDate).Replace(" 0:00:00", "");
        }
        else*/
          var directumRXDeliveryMethodSid = CitizenRequests.PublicFunctions.Module.Remote.GetDirectumRXDeliveryMethodSid();
          document = IncomingLetters.Create();
          document.DeliveryMethod = GovernmentSolution.MailDeliveryMethods.GetAll().Where(m => m.Sid == directumRXDeliveryMethodSid).FirstOrDefault();
          document.LeadingDocument = reasonDoc;
          
          if (OutgoingRequestLetters.Is(reasonDoc))
          {
            var docKind = Sungero.Docflow.DocumentKinds.GetAll().Where(k => k.Name == Resources.IncomingRequestLetter).FirstOrDefault();
            if (docKind != null)
              document.DocumentKind = docKind;
          }
        }
        
        var inResponseTo = Sungero.Docflow.OutgoingDocumentBases.As(reasonDoc).InResponseTo;
        document.InResponseTo = Sungero.Docflow.OutgoingDocumentBases.As(inResponseTo?.LeadingDocument);
        document.Correspondent = /*IncomingLetters.Is(reasonDoc) ? IncomingLetters.As(reasonDoc).Correspondent : */Companies.As(reasonDoc.BusinessUnit.Company);
        document.BusinessUnit = businessUnit;
        document.Addressee = businessUnit.CEO;
        document.Department = _obj.Registrar != null ? _obj.Registrar.Department : businessUnit.CEO.Department;
        //document.AddresseeDepartment = businessUnit.CEO.Department;
        if (reasonDoc.OurSignatory != null)
        {
          var contact = Sungero.Parties.Contacts.GetAll().Where(d => d.Person.Equals(reasonDoc.OurSignatory.Person) && d.Company.Equals(reasonDoc.BusinessUnit.Company)).FirstOrDefault();
          document.SignedBy = contact;
        }
        // Добавление возможности перенаправления входящих писем с помощью реализованного механизма.
        document.Dated = /*IncomingLetters.Is(reasonDoc) ? IncomingLetters.As(reasonDoc).Dated : */reasonDoc.RegistrationDate;
        document.InNumber = /*IncomingLetters.Is(reasonDoc) ? IncomingLetters.As(reasonDoc).InNumber : */reasonDoc.RegistrationNumber;
        var subjectCustom = reasonDocSubject;
        
        if (string.IsNullOrEmpty(reasonDocSubject) || string.IsNullOrWhiteSpace(reasonDocSubject))
          subjectCustom = document.Name;
        
        document.Subject = subjectCustom.Length > 250 ? subjectCustom.Remove(250) : subjectCustom;
        
        var reasonDocVersion = reasonDoc.LastVersion;
        document.CreateVersion();
        var documentVersion = document.LastVersion;
        
        // Копия тела документа.
        var docBodyStream = new System.IO.MemoryStream();
        using (var sourceStream = reasonDocVersion.Body.Read())
          sourceStream.CopyTo(docBodyStream);
        documentVersion.Body.Write(docBodyStream);
        docBodyStream.Close();
        
        if (reasonDoc.HasPublicBody == true)
        {
          var docPublicBodyStream = new System.IO.MemoryStream();
          using (var sourceStream = reasonDocVersion.PublicBody.Read())
            sourceStream.CopyTo(docPublicBodyStream);
          documentVersion.PublicBody.Write(docPublicBodyStream);
          docPublicBodyStream.Close();
        }
        documentVersion.AssociatedApplication = reasonDocVersion.AssociatedApplication;
        documentVersion.BodyAssociatedApplication = reasonDocVersion.BodyAssociatedApplication;
        
        
        document.Save();
        
        // Копия подписей.
        foreach (var signature in Signatures.Get(reasonDocVersion).Where(s => s.SignCertificate != null))
        {
          var signatureText = signature.GetDataSignature();
          try
          {
            Signatures.Import(document, signature.SignatureType, signature.SignatoryFullName, signatureText, documentVersion);
          }
          catch (Exception ex)
          {
            Logger.Error(ex.Message);
          }
        }
        
        // Привязка документов, заданных в источнике
        foreach (var addendumStr in _obj.Addendums.ToList())
        {
          var relatedDoc = addendumStr.Reason;
          if (Sungero.Docflow.Addendums.Is(relatedDoc))
          {
            if (!_obj.AddendaGroup.All.Any(d => d.Equals(relatedDoc)))
              _obj.AddendaGroup.ElectronicDocuments.Add(relatedDoc);
          }
          else
          {
            if (!_obj.OtherGroup.All.Any(d => d.Equals(relatedDoc)))
              _obj.OtherGroup.ElectronicDocuments.Add(relatedDoc);
          }
          
          if (addendumStr.Result == null)
          {
            // Создать копию приложений и вложить в задачу.
            var addendum = Sungero.Docflow.Addendums.As(relatedDoc);
            if (addendum != null)
            {
              var copyAddendum = PublicFunctions.Module.Remote.CopyAddendum(addendum);
              copyAddendum.LeadingDocument = document;
              copyAddendum.Save();
              // Перенести подписи.
              foreach (var signature in Signatures.Get(addendum).Where(s => s.SignCertificate != null))
              {
                var signatureText = signature.GetDataSignature();
                try
                {
                  Signatures.Import(copyAddendum, signature.SignatureType, signature.SignatoryFullName, signatureText, copyAddendum.LastVersion);
                }
                catch (Exception ex)
                {
                  Logger.Error(ex.Message);
                }
              }
              // Добавить к группе приложения к входящему документу.
              _obj.InAttachmentGroup.ElectronicDocuments.Add(copyAddendum);
              addendumStr.Result = copyAddendum;
            }
            else
            {
              relatedDoc.Relations.Add(Sungero.Docflow.PublicConstants.Module.SimpleRelationName, document);
              addendumStr.Result = relatedDoc;
            }
          }
        }
        
        var subject = Sungero.Docflow.PublicFunctions.Module.TrimSpecialSymbols(IncomingDocumentProcessingTasks.Resources.TaskSubject, document.Name);
        _obj.Subject = subject.Substring(0, subject.Length > 250 ? 250 : subject.Length);
        
        if (_obj.ResultDoc == null || _obj.ResultDoc != document)
        {
          _obj.ResultDoc = document;
          _obj.MainDocGroupReason.OfficialDocuments.Add(_obj.ReasonDoc);
          _obj.MainDocGroupNew.OfficialDocuments.Add(document);
        }
      }
      catch (Exception ex)
      {
        // В случае ошибок при создании, вместо стандартного уведомления отправляем собственное.
        var errorHandler = AsyncHandlers.HandleErrorInDocumentProcessingTask.Create();
        errorHandler.InternalMailRegisterId = _obj.GeneratedFrom.Id;
        errorHandler.ErrorMessage = ex.Message;
        errorHandler.ExecuteAsync();
      }
    }
  }
  #endregion

}