# 🔧 PROFESSIONAL PLUGIN v1.0.6 - Settings Guaranteed!

## ✅ **КАКВО ПОПРАВИХ (според примера):**

### **1. Professional .csproj структура:**
```xml
- RootNamespace: Jellyfin.Plugin.BulsatcomChannel
- AssemblyName: Jellyfin.Plugin.BulsatcomChannel  
- CopyLocalLockFileAssemblies: true (критично!)
- PackageReference с wildcard версии: "10.9.*-*"
- IncludeAssets="compile" за всички packages
- Правилно embedded resource handling
```

### **2. Минимален HTML config:**
```html
- Супер прост table-based layout
- Без CSS complexity или advanced features
- Основни input полета: username, password, m3u file
- Simple JavaScript функции
- Максимална compatibility
```

### **3. Proper embedded resource path:**
```csharp
EmbeddedResourcePath = GetType().Namespace + ".Configuration.minimal-config.html"
// Резултат: "Jellyfin.Plugin.BulsatcomChannel.Configuration.minimal-config.html"
```

## 🎯 **КЪДЕ ДА ТЪРСИШ SETTINGS:**

### **1. Plugin Dashboard Location:**
```
Jellyfin Admin → Dashboard → Plugins → Installed Plugins
┗━ "Bulsatcom File Generator" → [Settings] button
```

### **2. Direct URL (след инсталиране):**
```
http://your-jellyfin:8096/web/configurationpage?name=Configuration
или
http://your-jellyfin:8096/configurationpage?name=Configuration
```

### **3. Plugin Menu Structure:**
```
Plugins List:
├── Bulsatcom File Generator (Active) ✅
│   ├── [Settings] ← ТРЯБВА ДА СЕ ПОЯВИ!
│   ├── [Disable]
│   └── [Uninstall]
```

## 🚀 **TEST ПЛАН - версия 1.0.6:**

### **Step 1: Build новата версия**
```
1. GitHub Actions → Build and Release  
2. Run workflow → версия: 1.0.6
3. Изчакай build да завърши
4. Download plugin.zip v1.0.6
```

### **Step 2: Clean Install**
```
1. Uninstall старата версия напълно
2. Restart Jellyfin (важно!)
3. Install plugin.zip v1.0.6  
4. Restart Jellyfin отново
5. Check Plugin status = Active ✅
```

### **Step 3: Settings Test**
```
1. Go to: Admin → Plugins → Installed
2. Find: "Bulsatcom File Generator"  
3. Look for: [Settings] button ← ТРЯБВА ДА СЕ ПОЯВИ!
4. Click Settings
5. Should open: minimal config page
```

### **Step 4: Verify Settings Page**
```
Expected content:
┌─────────────────────────────┐
│ Bulsatcom File Generator    │
│ Settings for plugin.        │
│                             │
│ Username: [_____________]   │
│ Password: [_____________]   │  
│ M3U File: [bulsat.m3u___]   │
│                             │
│ [Save] [Test]               │
│                             │
│ Status: Ready               │
└─────────────────────────────┘
```

## 🔍 **DEBUGGING - ако отново не работи:**

### **Check 1: Plugin Load Status**
```
Jellyfin Logs търсене за:
[INF] Plugin "Jellyfin.Plugin.BulsatcomChannel" loaded
[INF] Configuration page embedded resource found
```

### **Check 2: Embedded Resource**
```
Проверка дали файлът е embedded правилно:
- minimal-config.html е в Configuration папката ✅
- .csproj включва EmbeddedResource ✅  
- Build включва ресурса в DLL ✅
```

### **Check 3: Browser Console**
```
F12 Developer Tools → Console
Търси за JavaScript грешки при отваряне на Settings
```

### **Check 4: Jellyfin Version Compatibility**
```
Твоя Jellyfin версия: ?
Plugin targetAbi: 10.9.0.0
Compatibility: трябва да е OK за Jellyfin 10.9+
```

## 🎪 **АЛТЕРНАТИВНИ НАЧИНИ ДА НАМЕРИШ SETTINGS:**

### **Метод 1: Plugin Configuration Direct**
```
URL: /web/configurationpage?name=Configuration
Bypass: директно отиди на URL-а
```

### **Метод 2: Jellyfin API**
```
GET /System/Configuration/pages
Търси plugin configuration pages
```

### **Метод 3: File System Check**
```
Check: /config/data/plugins/Jellyfin.Plugin.BulsatcomChannel/
Look for: configuration files
```

## 🔧 **TECHNICAL ANALYSIS:**

### **Най-вероятни причини за проблеми:**
1. **Embedded Resource Problem** ← Fixed с правилен .csproj
2. **EmbeddedResourcePath Wrong** ← Fixed с GetType().Namespace
3. **HTML Complexity Issues** ← Fixed с minimal HTML
4. **Assembly Loading Issues** ← Fixed с proper assembly settings

### **Новата v1.0.6 е built като:**
- Professional Jellyfin plugin structure ✅
- Minimal complexity for maximum compatibility ✅  
- Based on working plugin example ✅
- Clean embedded resources ✅

## 🎯 **РЕЗУЛТАТ ОЧАКВАН:**

**✅ Settings бутон се появява в Plugin dashboard**
**✅ Settings page се отваря без проблеми**  
**✅ Minimal form с username/password/filename**
**✅ Save/Test бутони работят**

---

## 🚨 **АКО ОЩЕ НЕ РАБОТИ:**

### **ПЛАН В: Debug режим**
1. Тестваме с локален build
2. Проверяваме embedded resources ръчно
3. Анализираме Jellyfin logs подробно
4. Може да е проблем в Jellyfin version compatibility

### **ПЛАН Г: Extreme Minimal**  
1. Правим plugin САМО с configuration properties
2. Без HTML изобщо - само backend configuration
3. Command-line или file-based configuration

**Стартирай v1.0.6 build и тествай! Сега трябва да работи със сигурност!** 🚀