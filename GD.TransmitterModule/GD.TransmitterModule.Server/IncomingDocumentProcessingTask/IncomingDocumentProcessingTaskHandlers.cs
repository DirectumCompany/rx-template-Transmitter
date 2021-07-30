using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;
using GD.TransmitterModule.IncomingDocumentProcessingTask;

namespace GD.TransmitterModule
{
  partial class IncomingDocumentProcessingTaskServerHandlers
  {

    public override void BeforeStart(Sungero.Workflow.Server.BeforeStartEventArgs e)
    {
      Functions.IncomingDocumentProcessingTask.ValidateTaskStarting(_obj);
      
      if (PublicFunctions.Module.CheckAttachmentIsValid(_obj))
        e.AddError(Resources.AttachIsNotValid);
    }

    public override void Created(Sungero.Domain.CreatedEventArgs e)
    {
      _obj.Subject = Sungero.Docflow.Resources.AutoformatTaskSubject;
    }
  }

}