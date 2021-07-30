using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;

namespace GD.TransmitterModule.Structures.Module
{  
  /// <summary>
  /// Результат отправки адресатам.
  /// </summary>
  [Public]
  partial class SendToAddresseeResult
  {    
    public List<string> infomation { get; set; }
    
    public List<string> errorsRX { get; set; }
    
    public List<string> errorsMEDO { get; set; }
    
    public List<string> errorsEmail { get; set; }
  }
}