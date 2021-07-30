using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;
using GD.TransmitterModule.MailRegister;

namespace GD.TransmitterModule
{
  partial class MailRegisterClientHandlers
  {

    public override void Showing(Sungero.Presentation.FormShowingEventArgs e)
    {
      _obj.State.Properties.TaskId.IsVisible = _obj.MailType == MailRegister.MailType.ActionItem ||
        _obj.MailType == MailRegister.MailType.SupervisorAssig ||
        _obj.MailType == MailRegister.MailType.ActionItemContr;
    }

  }
}