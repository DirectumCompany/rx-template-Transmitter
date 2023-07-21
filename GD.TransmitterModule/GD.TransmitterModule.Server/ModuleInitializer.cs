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
      CreateTransmitterSettings();
      CreateRoles();
      GrantRightsForAllUsers();
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
    
    /// <summary>
    /// Выдать права всем пользователям.
    /// </summary>
    public static void GrantRightsForAllUsers()
    {
      InitializationLogger.Debug("Init: Grant rights to all users.");
      var allUsers = Roles.AllUsers;
      
      // Настройки модуля отправки документов адресатам.
      TransmitterSettings.AccessRights.Grant(allUsers, DefaultAccessRightsTypes.Read);
      TransmitterSettings.AccessRights.Save();
    }
  }
}
