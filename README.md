# rx-template-transmitter
Репозиторий с шаблоном разработки "Отправка адресатам".

## Описание
Решение содержит механизм отправки исходящих писем адресатам по МЭДО, электронной почте или в рамках системы (RX-RX). При отправке у пользователя запрашивается перечень связанных документов для отправки их вместе с исходящим письмом.

Состав объектов разработки: 
* Справочник "Реестр отправки сообщений по Email" 
* Справочник "Реестр отправки сообщений в рамках системы"
* Задача "Обработка входящего документа"
* Функция модуля SendToAddressee
* Функция модуля CanSendToAddressee
> [!NOTE]
> Замечания и пожеланию по развитию шаблона разработки фиксируйте через [Issues](https://github.com/DirectumCompany/rx-template-Transmitter/issues).
При оформлении ошибки, опишите сценарий для воспроизведения. Для пожеланий приведите обоснование для описываемых изменений - частоту использования, бизнес-ценность, риски и/или эффект от реализации.
> 
> Внимание! Изменения будут вноситься только в новые версии.

## Варианты расширения функциональности на проектах

1. Перекрыть исходящее письмо для добавления кнопки для отправки адресатам.  
   Пример вычислений на кнопке (Выполнение):
```
  var result = GD.TransmitterModule.PublicFunctions.Module.SendToAddressee(_obj);
  if (result != null)
  {
    result.infomation.ForEach(e.AddInformation);
    result.errorsMEDO.ForEach(e.AddWarning);
    result.errorsRX.ForEach(e.AddWarning);
    result.errorsEmail.ForEach(e.AddWarning);
  }
```
   Пример вычислений на кнопке (Возможность выполнения):
```
  return GD.TransmitterModule.PublicFunctions.Module.CanSendToAddressee(_obj);
```
2. Локализовать название действия в истории документа, которое записывается при отправке документа.
   Пример локализации:
```
  <data name="Enum_Operation_SendAddressees" xml:space="preserve">
    <value>Отправка адресатам</value>
  </data>
```
3. Добавление возможности автоматического преобразования отправляемого документа в pdf и установки отметки об ЭП.
4. Добавление возможности перенаправления входящих писем с помощью реализованного механизма.
5. Добавление возможности отправки копии письма на указанный адрес электронной почты.

## Порядок установки
Перед использованием необходимо проверить актуальность сторонних библиотек используемых в данном шаблоне. Необходимо взять соответсвующие библиотеки из модуля "MEDO" решения Интеграция с МЭДО.

### Установка для ознакомления
1. Склонировать репозиторий Transmitter в папку.
2. Указать в _ConfigSettings.xml DDS:
```xml
<block name="REPOSITORIES">
  <repository folderName="Base" solutionType="Base" url="" />
  <repository folderName="RX" solutionType="Base" url="<адрес локального репозитория>" />
  <repository folderName="<Папка из п.1>" solutionType="Work" 
     url="https://github.com/DirectumCompany/rx-template-govtransmitter" />
</block>
```

### Установка для использования на проекте
Возможные варианты:

**A. Fork репозитория**
1. Сделать fork репозитория Transmitter для своей учетной записи.
2. Склонировать созданный в п. 1 репозиторий в папку.
3. Указать в _ConfigSettings.xml DDS:
``` xml
<block name="REPOSITORIES">
  <repository folderName="Base" solutionType="Base" url="" /> 
  <repository folderName="<Папка из п.2>" solutionType="Work" 
     url="<Адрес репозитория gitHub учетной записи пользователя из п. 1>" />
</block>
```

**B. Подключение на базовый слой.**

Вариант не рекомендуется, так как при выходе версии шаблона разработки не гарантируется обратная совместимость.
1. Склонировать репозиторий Transmitter в папку.
2. Указать в _ConfigSettings.xml DDS:
``` xml
<block name="REPOSITORIES">
  <repository folderName="Base" solutionType="Base" url="" /> 
  <repository folderName="<Папка из п.1>" solutionType="Base" 
     url="<Адрес репозитория gitHub>" />
  <repository folderName="<Папка для рабочего слоя>" solutionType="Work" 
     url="https://github.com/DirectumCompany/rx-template-govtransmitter" />
</block>
```

**C. Копирование репозитория в систему контроля версий.**

Рекомендуемый вариант для проектов внедрения.
1. В системе контроля версий с поддержкой git создать новый репозиторий.
2. Склонировать репозиторий Transmitter в папку с ключом `--mirror`.
3. Перейти в папку из п. 2.
4. Импортировать клонированный репозиторий в систему контроля версий командой:

`git push –mirror <Адрес репозитория из п. 1>`

