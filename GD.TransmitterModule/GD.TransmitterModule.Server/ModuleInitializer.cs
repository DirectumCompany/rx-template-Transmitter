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
      CreateTransmitterSettings();
      CreateRoles();
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
    
    /// <summary>
    /// Создать настройки модуля.
    /// </summary>
    public static void CreateTransmitterSettings()
    {
      var settings = Functions.Module.GetTransmitterSettings();
      if (settings == null)
        Functions.Module.CreateTransmitterSettings();
    }
    
    public static void CreateRoles()
    {
      // Роль "Ответственные за отправку писем по Email"
      // Если роль создается первый раз, то добавить в нее Администраторов.
      if (!Roles.GetAll(r => r.Sid == Constants.Module.EmailSendingResponsibleRoleGuid).Any())
      {
        var role = Sungero.Docflow.PublicInitializationFunctions.Module.CreateRole(GD.TransmitterModule.Resources.EmailSendingResponsibleRoleName, string.Empty, Constants.Module.EmailSendingResponsibleRoleGuid);
        role.RecipientLinks.AddNew().Member = Roles.Administrators;
        role.Save();
      }
    }
  }
}
