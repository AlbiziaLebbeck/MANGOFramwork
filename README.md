# 🛠️ Unity Project – MANGOFrameworkTemplate

This repository contains a Unity project developed with **Unity 2022.3.27f1 LTS**.  
The goal of this document is to guide collaborators through setup, workflow, and contribution standards.  

---

## 📦 Project Info
- **Unity Version**: 2022.3.27f1 LTS (replace with your version)  
- **Render Pipeline**: Built-in / URP / HDRP  
- **Target Platforms**: WebGL/Linux Dedicated Server
- **Source Control**: Git

---

## 🚀 Getting Started  

### 1. Clone the Repository  
```bash
git clone https://github.com/AlbiziaLebbeck/MANGOFramwork.git
```

### 2. Open in UnityHub
1. Open **Unity Hub**
2. Click **Add Project from Disk**
3. Select the cloned repository folder
4. Make sure you are using **Unity 2022.3.27f1 LTS** (see [Project Info](#-project-info))
)

## 📚 Dependencies

All required dependencies are already included in this repository.  
Collaborators do not need to install them manually.  

The following libraries/tools are used in this project:  

| Package / Tool                                                                  | Version  | Purpose          |
| ------------------------------------------------------------------------------- | -------- | ---------------- |
| [TextMeshPro](https://docs.unity3d.com/Packages/com.unity.textmeshpro@latest)   | Built-in | Text rendering   |
| [FishNet](https://github.com/FirstGearGames/FishNet) |  4.2.2R    | Multiplayer |
| [Starter Assets](https://assetstore.unity.com/packages/essentials/starter-assets-thirdperson-updates-in-new-charactercontroller-pa-196526)              | 1.15  | Player Movement   |
| [glTFast](https://github.com/atteneder/glTFast)      | 6.4.0      | GLTF Loader |
| [Parrel Sync](https://github.com/VeriorPies/ParrelSync/)      | 1.5.2      | Unity editor extension to test multiplayer  |

---

## 🧪 Testing & Debugging

### Cloning Unity Editor
Cloning editor is very useful for mulitplayer game development, you can test client and server simultanousely without building the project

To Clone Unity Editor

- Menu ParrelSync > Clones Manager
- In Inspector window, click Create new clone

<img src="https://github.com/AlbiziaLebbeck/IntaniaVerse/assets/61304577/a4927839-81ea-4821-b097-59eb2fff5512" alt="Create new clone" width="480" style="display:block; margin:auto;"/>

- Then click Open in New Editor

<img src="https://github.com/AlbiziaLebbeck/IntaniaVerse/assets/61304577/64bfa0db-f3cc-41cd-bae0-8914980efbee" alt="Open in New Editor" width="480" style="display:block; margin:auto;"/>

- This will create new folder at the same as your project directory

<img src="https://github.com/AlbiziaLebbeck/IntaniaVerse/assets/61304577/dfb4e75f-644a-4c76-8ab0-15d0f1cd0c73" alt="Project Directory" width="480" style="display:block; margin:auto;"/>

- Make sure you open the original one from UnityHub

<img src="https://github.com/AlbiziaLebbeck/IntaniaVerse/assets/61304577/af263e89-e458-4c3f-bc24-ea0453cad2f4" alt="UnityHub" width="480" style="display:block; margin:auto;"/>
