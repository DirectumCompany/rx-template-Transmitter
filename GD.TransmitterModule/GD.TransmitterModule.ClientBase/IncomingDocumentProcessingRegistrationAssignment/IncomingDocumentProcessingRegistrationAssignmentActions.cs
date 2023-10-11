using System;
using System.Collections.Generic;
using System.Linq;
using Sungero.Core;
using Sungero.CoreEntities;
using GD.TransmitterModule.IncomingDocumentProcessingRegistrationAssignment;

namespace GD.TransmitterModule.Client
{
  partial class IncomingDocumentProcessingRegistrationAssignmentActions
  {
    public virtual void RedirectToBusinessUnit(Sungero.Workflow.Client.ExecuteResultActionArgs e)
    {
      if (_obj.SendTo != null)
      {
        var task = IncomingDocumentProcessingTasks.As(_obj.Task);
        _obj.ToBusinessUnitBefore = task.ToBusinessUnit;
        _obj.ToBusinessUnit = _obj.SendTo;
        _obj.ToCounterparty = _obj.SendTo.Company;
      }
      else
      {
        _obj.State.Properties.SendTo.HighlightColor = Colors.Common.Red;
        e.AddError(IncomingDocumentProcessingRegistrationAssignments.Resources.RedirectToBusUnitError);
      }
    }

    public virtual bool CanRedirectToBusinessUnit(Sungero.Workflow.Client.CanExecuteResultActionArgs e)
    {
      var task = IncomingDocumentProcessingTasks.As(_obj.Task);
      return !Functions.IncomingDocumentProcessingRegistrationAssignment.IsMainDocumentRegistered(_obj) && task.Registrar == null;
    }

    public virtual void RedirectToDepartment(Sungero.Workflow.Client.ExecuteResultActionArgs e)
    {
      var task = IncomingDocumentProcessingTasks.As(_obj.Task);
      var document = task.MainDocGroupNew.OfficialDocuments.FirstOrDefault();
      
      Logger.DebugFormat("1");
      if (document != null && document.BusinessUnit != null)
      {
        Logger.DebugFormat("2");
        
        var employees = Sungero.Docflow.RegistrationGroups.GetAll(x => x.ResponsibleEmployee.Department != null &&
                                                                  x.ResponsibleEmployee.Department.BusinessUnit == document.BusinessUnit &&
                                                                  x.CanRegisterIncoming == true &&
                                                                  x.ResponsibleEmployee.Status == Sungero.Company.Employee.Status.Active &&
                                                                  x.Status == Sungero.Docflow.RegistrationGroup.Status.Active).
          Select(x => x.ResponsibleEmployee).ToList();
        if (!employees.Any())
          return;
        
        Logger.DebugFormat("3");
        var dialog = Dialogs.CreateInputDialog(IncomingDocumentProcessingRegistrationAssignments.Resources.RedirectToDepartment);
        Logger.DebugFormat("4");
        var addressee = dialog.AddSelect(IncomingDocumentProcessingRegistrationAssignments.Resources.SelectAddresseeToRedirect, true, Sungero.Company.Employees.Null).
          From(employees.Distinct().ToArray());
        Logger.DebugFormat("5");
        if (dialog.Show() == DialogButtons.Ok)
        {
          Logger.DebugFormat("6");
          var documentSettings = Sungero.Docflow.RegistrationSettings.GetAll().Where(s => s.SettingType == Sungero.Docflow.RegistrationSetting.SettingType.Registration &&
                                                                                     s.Status == Sungero.Docflow.RegistrationSetting.Status.Active &&
                                                                                     s.DocumentKinds.Any(t => Equals(t.DocumentKind, document.DocumentKind)) &&
                                                                                     s.BusinessUnits.Any(t => Equals(t.BusinessUnit, document.BusinessUnit))).FirstOrDefault();
          Logger.DebugFormat("7");
          if (documentSettings != null)
          {
            Logger.DebugFormat("8");
            _obj.Registrar = addressee.Value;
          }
          else
            e.AddError(IncomingDocumentProcessingRegistrationAssignments.Resources.NotResponsible);
        }
        else
          e.Cancel();
      }
      else
        e.AddError(IncomingDocumentProcessingRegistrationAssignments.Resources.CheckDocumentAndRegistrationGroup);
    }

    public virtual bool CanRedirectToDepartment(Sungero.Workflow.Client.CanExecuteResultActionArgs e)
    {
      return !Functions.IncomingDocumentProcessingRegistrationAssignment.IsMainDocumentRegistered(_obj);
    }

    public virtual void Rework(Sungero.Workflow.Client.ExecuteResultActionArgs e)
    {
      if (string.IsNullOrWhiteSpace(_obj.ActiveText))
        e.AddError(IncomingDocumentProcessingRegistrationAssignments.Resources.NeedFillReasonForRework);
    }

    public virtual bool CanRework(Sungero.Workflow.Client.CanExecuteResultActionArgs e)
    {
      return !Functions.IncomingDocumentProcessingRegistrationAssignment.IsMainDocumentRegistered(_obj);
    }

    public virtual void Register(Sungero.Workflow.Client.ExecuteResultActionArgs e)
    {
      var document = _obj.MainDocGroupNew.OfficialDocuments.FirstOrDefault();
      if (document == null || document.RegistrationState != Sungero.Docflow.OfficialDocument.RegistrationState.Registered)
      {
        e.AddError(Resources.DocumentNotRegistered);
        return;
      }
    }

    public virtual bool CanRegister(Sungero.Workflow.Client.CanExecuteResultActionArgs e)
    {
      return true;
    }

    public virtual void Registration(Sungero.Domain.Client.ExecuteActionArgs e)
    {
      var document = IncomingDocumentProcessingTasks.As(_obj.Task).MainDocGroupNew.OfficialDocuments.FirstOrDefault();
      if (document != null)
        document.ShowModal();
      else
        e.AddError(IncomingDocumentProcessingRegistrationAssignments.Resources.DocumentNotFound);
    }

    public virtual bool CanRegistration(Sungero.Domain.Client.CanExecuteActionArgs e)
    {
      return !Functions.IncomingDocumentProcessingRegistrationAssignment.IsMainDocumentRegistered(_obj);
    }

  }

}