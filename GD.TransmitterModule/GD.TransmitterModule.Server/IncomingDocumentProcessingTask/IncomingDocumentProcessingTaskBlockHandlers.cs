using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;
using Sungero.Workflow;
using GD.TransmitterModule.IncomingDocumentProcessingTask;
using GD.GovernmentSolution;
using GD.CitizenRequests;

namespace GD.TransmitterModule.Server.IncomingDocumentProcessingTaskBlocks
{
  partial class SetCounterpartyStateHandlers
  {

    public virtual void SetCounterpartyStateExecute()
    {
      Functions.Module.ChangeDocumentStateInfoInRegister(_obj.GeneratedFrom, _block.DocumentState, _block.Comment, _block.SaveOldComment == true);
      
      if (_block.IsCorrespondentChanged == true && _obj.ReasonDoc != null)
      {
        var newItem = InternalMailRegisters.Create();
        newItem.LeadingDocument = _obj.ReasonDoc;
        newItem.Correspondent = _obj.ToCounterparty;
        foreach (var row in _obj.GeneratedFrom.RelatedDocuments)
          newItem.RelatedDocuments.AddNew().Document = row.Document;
        
        newItem.Status = GD.TransmitterModule.InternalMailRegister.Status.Complete;
        newItem.TaskId = _obj.GeneratedFrom.TaskId;
        newItem.SyncStateInDocument = GD.TransmitterModule.InternalMailRegister.SyncStateInDocument.ToProcess;
        newItem.CounterpartyState = Resources.DeliveryState_Sent;
        newItem.IsRedirect = true;
        newItem.SaveOldComment = _block.SaveOldComment;
        newItem.Save();
        
        _obj.GeneratedFrom = newItem;
      }
    }
  }

  partial class IncomingDocumentProcessingReworkAssignmentHandlers
  {

    public virtual void IncomingDocumentProcessingReworkAssignmentCompleteAssignment(GD.TransmitterModule.IIncomingDocumentProcessingReworkAssignment assignment)
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
    }

    public virtual void IncomingDocumentProcessingReworkAssignmentStart()
    {
      var document = _obj.ReasonDoc;
      _block.IsParallel = true;
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
      if (sender == null || sender.IsSystem == true)
        sender = PublicFunctions.Module.Remote.GetRegistrarForBusinessUnit(document.BusinessUnit, document.DocumentKind);
      _block.Performers.Add(sender);
      _obj.AccessRights.Grant(sender, DefaultAccessRightsTypes.Change);
    }
  }

  partial class IncomingDocumentProcessingRegistrationAssignmentHandlers
  {

    public virtual void IncomingDocumentProcessingRegistrationAssignmentCompleteAssignment(GD.TransmitterModule.IIncomingDocumentProcessingRegistrationAssignment assignment)
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
      
      if (result == GD.TransmitterModule.IncomingDocumentProcessingRegistrationAssignment.Result.Rework)
        _obj.ReworkText = assignment.ActiveText;
      
      if (result == GD.TransmitterModule.IncomingDocumentProcessingRegistrationAssignment.Result.RedirectToBusinessUnit)
      {
        _obj.ToBusinessUnitBefore = assignment.ToBusinessUnitBefore;
        _obj.ToBusinessUnit = assignment.ToBusinessUnit;
        _obj.ToCounterparty = assignment.ToCounterparty;
      }
      if (result == GD.TransmitterModule.IncomingDocumentProcessingRegistrationAssignment.Result.RedirectToDepartment)
      {
        _obj.Registrar = assignment.Registrar;
      }
    }

    public virtual void IncomingDocumentProcessingRegistrationAssignmentStart()
    {
      var docKindIncLetter = Sungero.Docflow.DocumentKinds.GetAll(x => x.DocumentType.DocumentTypeGuid.ToLower() == PublicConstants.Module.IncLetterKind.ToLower()
                                                                  && x.IsDefault == true).FirstOrDefault();
      // Определить регистратора и выдать ему права
      _block.IsParallel = true;
      var registrator = _obj.Registrar;
      if (registrator == null)
        registrator = PublicFunctions.Module.Remote.GetRegistrarForBusinessUnit(_obj.ToBusinessUnit, docKindIncLetter);
      _block.Performers.Add(registrator);
    }
  }

  partial class CreateSynchronizeIncomingDocumentHandlers
  {

    public virtual void CreateSynchronizeIncomingDocumentExecute()
    {
      try
      {
        var reasonDoc = _obj.ReasonDoc;
        var reasonDocSubject = string.Empty;
        var secCategory = GD.MEDO.SecurityCategories.Null;
        if (OutgoingLetters.Is(reasonDoc))
        {
          var reasonOutgoingLetter = OutgoingLetters.As(reasonDoc);
          reasonDocSubject = reasonOutgoingLetter.Subject;
          secCategory = reasonOutgoingLetter.SecCategoryGD;
        }
        if (OutgoingRequestLetters.Is(reasonDoc))
        {
          var reasonOutgoingRequestLetter = OutgoingRequestLetters.As(reasonDoc);
          reasonDocSubject = reasonOutgoingRequestLetter.Subject;
          secCategory = reasonOutgoingRequestLetter.SecCategory;
        }
        
        if (string.IsNullOrEmpty(reasonDocSubject) || string.IsNullOrWhiteSpace(reasonDocSubject))
          reasonDocSubject = reasonDoc.Subject;
        
        var document = IncomingLetters.As(_obj.ResultDoc);
        var businessUnit = _obj.ToBusinessUnit;
        // Создать/обновить входящее письмо
        if (document == null)
        {
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
        document.Correspondent = Companies.As(reasonDoc.BusinessUnit.Company);
        document.BusinessUnit = businessUnit;
        document.SecCategoryGD = secCategory;
        document.Addressee = businessUnit.CEO;
        document.Department = _obj.Registrar != null ? _obj.Registrar.Department : businessUnit.CEO.Department;
        if (reasonDoc.OurSignatory != null)
        {
          var contact = Sungero.Parties.Contacts.GetAll().Where(d => d.Person.Equals(reasonDoc.OurSignatory.Person) && d.Company.Equals(reasonDoc.BusinessUnit.Company)).FirstOrDefault();
          document.SignedBy = contact;
        }
        // Добавление возможности перенаправления входящих писем с помощью реализованного механизма.
        document.Dated = reasonDoc.RegistrationDate;
        document.InNumber = reasonDoc.RegistrationNumber;
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
          
          var isSimpleRelation = reasonDoc.Relations.GetRelatedDocuments(Sungero.Docflow.PublicConstants.Module.SimpleRelationName)
            .Any(d => d.Equals(relatedDoc));
          var isAddendumRelation = reasonDoc.Relations.GetRelatedDocuments(Sungero.Docflow.PublicConstants.Module.AddendumRelationName)
            .Any(d => d.Equals(relatedDoc));
          
          if (isAddendumRelation)
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
            if (isAddendumRelation)
            {
              var isAddendum = Sungero.Docflow.Addendums.Is(relatedDoc);
              // Создать копию приложений и вложить в задачу.
              Sungero.Content.IElectronicDocument copiedDocument = null;
              
              if (isAddendum)
              {
                var copiedAddendum = PublicFunctions.Module.Remote.CopyAddendum(Sungero.Docflow.Addendums.As(relatedDoc));
                copiedAddendum.LeadingDocument = document;
                copiedDocument = Sungero.Content.ElectronicDocuments.As(copiedAddendum);
              }
              else
                copiedDocument = PublicFunctions.Module.Remote.CopyElectronicDocument(Sungero.Content.ElectronicDocuments.As(relatedDoc));
              
              copiedDocument.Save();

              // Перенести подписи.
              foreach (var signature in Signatures.Get(relatedDoc).Where(s => s.SignCertificate != null))
              {
                var signatureText = signature.GetDataSignature();
                try
                {
                  Signatures.Import(copiedDocument, signature.SignatureType, signature.SignatoryFullName, signatureText, copiedDocument.LastVersion);
                }
                catch (Exception ex)
                {
                  Logger.Error(ex.Message);
                }
              }
              
              // Добавить к группе приложения к входящему документу.
              _obj.InAttachmentGroup.ElectronicDocuments.Add(copiedDocument);
              copiedDocument.Relations.AddFrom(Sungero.Docflow.PublicConstants.Module.AddendumRelationName, document);
              addendumStr.Result = copiedDocument;
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
}