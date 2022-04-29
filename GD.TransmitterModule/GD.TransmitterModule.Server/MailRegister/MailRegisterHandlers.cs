using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;
using GD.TransmitterModule.MailRegister;

namespace GD.TransmitterModule
{
  partial class MailRegisterServerHandlers
  {

    public override void Created(Sungero.Domain.CreatedEventArgs e)
    {
      _obj.Iteration = 0;
    }

    public override void BeforeSave(Sungero.Domain.BeforeSaveEventArgs e)
    {
      // Отправить уведомление об ошибке отправки ответственным
      if (_obj.Status == Status.Error && _obj.State.Properties.Status.IsChanged)
      {
        Functions.Module.SendNoticeToResponsible(_obj);
      }
    }
  }

}