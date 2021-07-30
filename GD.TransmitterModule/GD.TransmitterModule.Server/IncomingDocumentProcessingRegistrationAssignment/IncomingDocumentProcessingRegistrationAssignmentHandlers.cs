using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;
using GD.TransmitterModule.IncomingDocumentProcessingRegistrationAssignment;

namespace GD.TransmitterModule
{
  partial class IncomingDocumentProcessingRegistrationAssignmentSendToSearchPropertyFilteringServerHandler<T>
  {

    public virtual IQueryable<T> SendToSearchDialogFiltering(IQueryable<T> query, Sungero.Domain.PropertySearchDialogFilteringEventArgs e)
    {
      e.DisableUiFiltering = true;
			return query;
    }
  }

  partial class IncomingDocumentProcessingRegistrationAssignmentSendToPropertyFilteringServerHandler<T>
  {

    public virtual IQueryable<T> SendToFiltering(IQueryable<T> query, Sungero.Domain.PropertyFilteringEventArgs e)
    {
      e.DisableUiFiltering = true;
			return query;
    }
  }

}