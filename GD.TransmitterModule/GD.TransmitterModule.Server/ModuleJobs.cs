using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;

namespace GD.TransmitterModule.Server
{
  public class ModuleJobs
  {

    /// <summary>
    /// Исходящие. Отправка сообщений по e-mail.
    /// </summary>
    public virtual void SendOutgoingMail()
    {
      foreach (var item in MailRegisters.GetAll(x => x.Status == GD.TransmitterModule.MailRegister.Status.ToProcess &&
                                                (x.MailType == null || x.MailType == GD.TransmitterModule.MailRegister.MailType.OutgoingLetter)))
      {
        if (Locks.GetLockInfo(item).IsLocked)
          continue;
        Functions.Module.SendDocumentAddresseesEMail(item);
      }
    }

    /// <summary>
    /// Исходящие. Отправка сообщений RX-RX.
    /// </summary>
    public virtual void SendOutgoingInternalMail()
    {
      foreach (var item in InternalMailRegisters.GetAll(x => x.Status == GD.TransmitterModule.InternalMailRegister.Status.ToProcess))
      {
        if (Locks.GetLockInfo(item).IsLocked)
          continue;
        Functions.Module.SendInternalMail(item);
      }
    }

  }
}