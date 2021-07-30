using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;
using GD.TransmitterModule.IncomingDocumentProcessingTask;

namespace GD.TransmitterModule.Server
{
  partial class IncomingDocumentProcessingTaskFunctions
  {

    /// <summary>
    /// Проверить параметры при старте задачи.
    /// </summary>
    public virtual void ValidateTaskStarting()
    {
      if (_obj.ToCounterparty == null)
        throw new NullReferenceException("Не заполнен обязательный параметр 'Корреспондент'.");
      
      if (_obj.ToBusinessUnit == null)
        throw new NullReferenceException("Не заполнен обязательный параметр 'В нашу организацию'.");
      
    }

  }
}