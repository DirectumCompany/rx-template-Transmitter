using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;
using GD.TransmitterModule.IncomingDocumentProcessingRegistrationAssignment;

namespace GD.TransmitterModule.Server
{
  partial class IncomingDocumentProcessingRegistrationAssignmentFunctions
  {

    /// <summary>
    /// Проверить зарегистрирован или нет входящий документ.
    /// </summary>    
    [Remote(IsPure = true)]
    public bool IsMainDocumentRegistered()
    {
      var document = IncomingDocumentProcessingTasks.As(_obj.Task)?.MainDocGroupNew.OfficialDocuments.FirstOrDefault();
      return document.RegistrationState == Sungero.Docflow.OfficialDocument.RegistrationState.Registered;
    }

  }
}