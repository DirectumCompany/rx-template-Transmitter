using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;
using Sungero.Domain.Initialization;

namespace GD.TransmitterModule.Server
{
  public partial class ModuleInitializer
  {

    public override void Initializing(Sungero.Domain.ModuleInitializingEventArgs e)
    {
      CreateDeliveryMethods();
    }
    
    public static void CreateDeliveryMethods()
    {
      CreateElectronicDeliveryMethod(Resources.DirectumRXDeliveryMethod, Constants.Module.DeliveryMethod.DirectumRX);
    }
    
    /// <summary>
    /// Создать способ доставки.
    /// </summary>
    /// <param name="name">Название.</param>
    /// <param name="sid">Уникальный ИД, регистрозависимый.</param>
    [Public]
    public static void CreateElectronicDeliveryMethod(string name, string sid)
    {
      var method = string.IsNullOrEmpty(sid) ? GD.GovernmentSolution.MailDeliveryMethods.GetAll(m => m.Name == name).FirstOrDefault() :
        GD.GovernmentSolution.MailDeliveryMethods.GetAll(m => m.Sid == sid).FirstOrDefault();
      if (method == null)
      {
        method = GD.GovernmentSolution.MailDeliveryMethods.Create();
        method.Sid = sid;
      }
      method.Name = name;
      method.CommunicationForm = GD.GovernmentSolution.MailDeliveryMethod.CommunicationForm.Electronic;
      method.Save();
    }
  }
}
