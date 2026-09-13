# Project context
`izbushka_simulation` — Unity-проект (C#) виртуального 3D-симулятора робота «Избушка»: физика шасси, датчики, виртуальная камера, обмен командами с веб-ядром по TCP.

## What to review and what to ignore
Ревьюить: код в `Assets/Scripts/` (C#), изменения `ProjectSettings/` только если явно меняют логику (например, физические слои/теги), `*.csproj`/`*.sln` только при структурных изменениях проекта.
Игнорировать: `.github/`, CI-конфиги, `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `Packages/` (генерируемые Unity папки и кэши), сцены/префабы и другие бинарные ассеты Unity (`.unity`, `.prefab`, `.asset`) — если только не содержат читаемых текстовых диффов с очевидными логическими проблемами.
Если в диффе нет ревьюабельного кода (только бинарные/generated файлы) — так и напиши в summary, не выдумывай замечания.

## Always read the PR description and comments
Перед ревью прочитай PR DESCRIPTION из PR / GIT CONTEXT и комментарии в pr-comments/others/. Не поднимай повторно то, что там уже решено или объяснено; объяснение снимает придирку, но не отменяет реальный баг.

## Stack
C#, Unity Engine, TCP-сервер/клиент для локального протокола VirtualLink, JSON (обычно `Newtonsoft.Json` или `JsonUtility`).

## Code style
Стандартные конвенции Unity C# (PascalCase для публичных членов/методов, `[SerializeField]` для приватных полей в инспекторе), избегать блокирующих операций в `Update()`/`FixedUpdate()`.

## Architecture and patterns
- `IzbushkaCarController.cs` — физика танкового шасси: масса 1200 кг, заниженный центр масс, 4×`WheelCollider`, пониженная `sidewaysFriction.stiffness` (0.04) для разворота на месте, моторные команды → `motorTorque`/`brakeTorque` по бортам.
- TCP-сервер на порт 5470, кадрирование сообщений 4-байтовым Big-Endian заголовком длины + JSON UTF-8.
- Обработка команд `{"type": "command", ...}`, подписок на датчики `{"type": "subscribe", ...}`, публикация `{"type": "subscription_data", ...}`.
- Виртуальная камера: рендер в RenderTexture → JPEG → Base64 → `{"type": "frame", ...}`.

## Dependencies on other parts of the system
- Единственный потребитель/клиент — [izbushka-web-core](https://github.com/Emeteil/izbushka-web-core), `VirtualLinkSubscriber` (приоритет 50 в `TransportBus`) подключается по TCP на localhost:5470, используется как fallback, когда реальное железо (STM32) недоступно.

## Review checklist
- Изменение формата TCP-сообщений (поля `type`, `command`, `action`, `kwargs`, структура `frame`/`subscription_data`) без синхронизации с обработчиком `VirtualLinkSubscriber` в [izbushka-web-core](https://github.com/Emeteil/izbushka-web-core) — обязательно отметить.
- Изменение порта (5470) или формата кадрирования (4-байтовый Big-Endian префикс длины) — ломает совместимость с клиентом.
- Изменения физики шасси (масса, центр масс, трение колёс), которые могут расходиться с реальным поведением робота, задокументированным в Architecture_Documentation — не блокирующее, но стоит отметить как риск рассинхронизации симуляции с реальностью.
- Утечки памяти/незакрытые сокеты, блокирующие вызовы в игровом цикле (`Update`), влияющие на FPS.
- Случайно закоммиченные файлы из `Library/`/`Temp/` — если попали в дифф, стоит указать, что их лучше исключить через `.gitignore`.
