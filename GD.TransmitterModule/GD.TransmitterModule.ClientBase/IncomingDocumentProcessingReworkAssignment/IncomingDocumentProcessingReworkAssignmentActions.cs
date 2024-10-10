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
      var documents = _obj.AddendaGroup.ElectronicDocuments.Where(doc => Sungero.Content.ElectronicDocuments.Is(doc)).ToList();
      var documentProcessingTask = IncomingDocumentProcessingTasks.As(_obj.Task).AddendaGroup.ElectronicDocuments.ToList();

      foreach (var document in documents.Except(documentProcessingTask))
      {
        var addendumsIncomingDocumentProcessingTasks = IncomingDocumentProcessingTasks.As(_obj.Task).Addendums.AddNew();
        addendumsIncomingDocumentProcessingTasks.Reason = document;
        document.Relations.AddFrom(Sungero.Docflow.PublicConstants.Module.AddendumRelationName,
                                   IncomingDocumentProcessingTasks.As(_obj.Task).MainDocGroupReason.OfficialDocuments.FirstOrDefault());
      }
    }

    public virtual bool CanCorrected(Sungero.Workflow.Client.CanExecuteResultActionArgs e)
    {
      return true;
    }

  }
}