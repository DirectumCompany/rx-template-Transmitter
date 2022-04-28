using System;
using Sungero.Core;

namespace GD.TransmitterModule.Constants
{
  public static class Module
  {

    /// <summary>
    /// Идентификатор роли "Ответственные за отправку на Email".
    /// </summary>
    [Public]
    public static readonly Guid EmailSendingResponsibleRoleGuid = Guid.Parse("B405A458-2F6A-4D43-8B07-3605967CAB62");
    
    // ГУИДы способов доставки.
    public static class DeliveryMethod
    {
      [Public]
      public const string DirectumRX = "DB866210-2F13-4B7F-AD6C-1857AA078843";
    }
    
    /// <summary>
    /// Действие в историю "Отправка адресатам".
    /// </summary>
    public const string SendAddressees = "SendAddressees";
    
    /// <summary>
    /// Гуид входящего письма.
    /// </summary>
    [Public]
    public const string IncLetterKind = "8dd00491-8fd0-4a7a-9cf3-8b6dc2e6455d";
  }
}