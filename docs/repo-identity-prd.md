# 🚀 PRD: Repo-Aware Oh My Posh Configuration Generator (.NET Tool)

## 📌 Overview
This project aims to build a .NET CLI tool that analyzes local repositories and generates Oh My Posh configuration files to provide a consistent, visual identity per repository in the terminal.

## 🎯 Goals
- Generate Oh My Posh configuration from repository metadata
- Improve developer context awareness
- Provide reusable, version-controlled configs
- Integrate with https://github.com/elbruno/nuget-repo-dashboard

## 🚫 Non-Goals
- No dynamic switching on `cd`
- No GUI
- No Windows Terminal plugin

## 🧩 Key Features (v1)
- Scan folders for Git repos
- Detect repo name, path, branch, dirty state
- Assign deterministic colors
- Generate Oh My Posh JSON config

## 📥 Inputs
- Root folder path
- Optional repo.identity.json

## 📤 Outputs
- oh-my-posh.generated.json

## 🧠 Metadata Example
```json
{
  "name": "ElBruno.LocalEmbeddings",
  "type": "library",
  "accentColor": "#0078D4",
  "icon": "🧠"
}
```

## 🖥️ CLI Commands
```
repo-identity scan <path>
repo-identity generate
repo-identity apply
repo-identity preview
```

## 🏗️ Architecture
- Repo Scanner
- Metadata Loader
- Color Generator
- Config Generator
- CLI Layer

## 📁 Structure
```
/src
/docs
/terminal/ohmyposh
```

## 🔮 Future
- Windows Terminal integration
- GitHub metadata
- Multi-theme support

## ✅ Success Criteria
- Config generated
- Clear repo differentiation
- Easy reuse

## 🚀 Next Steps
1. Create .NET CLI
2. Scan repos
3. Generate config
