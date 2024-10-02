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
      var documents = _obj.AddendaGroup.ElectronicDocuments.Where(doc => Sungero.Content.ElectronicDocuments.Is(doc))
        .Union(_obj.OtherGroup.ElectronicDocuments).ToList();

      foreach (var document in documents)
      {
        if (!IncomingDocumentProcessingTasks.As(_obj.Task).Addendums.Where(d => d.Reason.Equals(document)).Any())
        {
          var addendumsIncomingDocumentProcessingTasks = IncomingDocumentProcessingTasks.As(_obj.Task).Addendums.AddNew();
          addendumsIncomingDocumentProcessingTasks.Reason = document;
          if (_obj.AddendaGroup.ElectronicDocuments.Where(d => Equals(d, document)).Any())
            document.Relations.AddFrom(Sungero.Docflow.PublicConstants.Module.AddendumRelationName,
                                       IncomingDocumentProcessingTasks.As(_obj.Task).MainDocGroupReason.OfficialDocuments.FirstOrDefault());
          else
          {
            document.Relations.AddFrom(Sungero.Docflow.PublicConstants.Module.SimpleRelationName,
                                       IncomingDocumentProcessingTasks.As(_obj.Task).MainDocGroupReason.OfficialDocuments.FirstOrDefault());
          }
        }
      }
    }

    public virtual bool CanCorrected(Sungero.Workflow.Client.CanExecuteResultActionArgs e)
    {
      return true;
    }

  }


}