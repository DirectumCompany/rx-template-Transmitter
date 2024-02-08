using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;
using GD.TransmitterModule.IncomingDocumentProcessingReworkAssignment;

namespace GD.TransmitterModule.Client
{
  partial class IncomingDocumentProcessingReworkAssignmentActions
  {
    public virtual void NotSend(Sungero.Workflow.Client.ExecuteResultActionArgs e)
    {
      
    }

    public virtual bool CanNotSend(Sungero.Workflow.Client.CanExecuteResultActionArgs e)
    {
      return true;
    }

    public virtual void Corrected(Sungero.Workflow.Client.ExecuteResultActionArgs e)
    {
      var documents = _obj.AddendaGroup.ElectronicDocuments.Where(x => Sungero.Content.ElectronicDocuments.Is(x)).ToList();
      var documentProcessingTask = IncomingDocumentProcessingTasks.As(_obj.MainTask).AddendaGroup.ElectronicDocuments.ToList();
      var addendums = new List<Sungero.Docflow.IAddendum>();
      
      foreach (var document in documents.Except(documentProcessingTask))
      {
        if (Sungero.Content.ElectronicDocuments.Is(document))
        {
          var createdAddendum = Sungero.Docflow.Addendums.Create();
          
          if (!createdAddendum.HasVersions)
            createdAddendum.CreateVersion();
          
          using (var docBodyStream = new System.IO.MemoryStream())
          {
            using (var sourceStream = document.LastVersion.Body.Read())
              sourceStream.CopyTo(docBodyStream);

            createdAddendum.LastVersion.Body.Write(docBodyStream);
          }
          
          createdAddendum.LastVersion.AssociatedApplication = document.AssociatedApplication;
          createdAddendum.LeadingDocument = Sungero.Docflow.OfficialDocuments.As(document);
          createdAddendum.Save();
          addendums.Add(createdAddendum);
        }
      }
      
      foreach (var addendum in addendums)
      {
        var addendumsIncomingDocumentProcessingTasks = IncomingDocumentProcessingTasks.As(_obj.MainTask).Addendums.AddNew();
        addendumsIncomingDocumentProcessingTasks.Reason = addendum;
      }
    }

    public virtual bool CanCorrected(Sungero.Workflow.Client.CanExecuteResultActionArgs e)
    {
      return true;
    }

  }


}