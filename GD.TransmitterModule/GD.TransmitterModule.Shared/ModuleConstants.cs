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