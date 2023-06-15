using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;
using GD.TransmitterModule.InternalMailRegister;

namespace GD.TransmitterModule
{
  partial class InternalMailRegisterServerHandlers
  {

    public override void Created(Sungero.Domain.CreatedEventArgs e)
    {
      _obj.IsRequestTransfer = false;
    }
  }

}