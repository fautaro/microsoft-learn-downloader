# ml-downloader

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Playwright](https://img.shields.io/badge/Playwright-1.62.0-2EAD33?logo=playwright)](https://playwright.dev/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

> A modern .NET console tool to download and export Microsoft Learn courses, learning paths, and modules into clean, consolidated, offline-ready PDFs.

---

## Overview

Ever wanted to study **Microsoft Learn** material offline during a flight, on a tablet/e-reader, or keep an organized PDF archive of a certification path?

**ml-downloader** automates the entire process: it navigates full courses, discovers all learning paths and modules, strips away web clutter (navbars, headers, sidebars, interactive controls), forces video-based lessons into readable text format, and compiles each module into a polished, unified A4 PDF.

---

## Features

- **Full Hierarchy Support**:
  - **Courses (`/courses/`)**: Recursively crawls all linked learning paths and their modules.
  - **Learning Paths (`/paths/`)**: Crawls and exports all modules belonging to the path.
  - **Single Modules (`/modules/`)**: Directly processes individual modules into a single PDF.
- **Modern Headless Browser (Playwright)**:
  - Powered by **Microsoft Playwright (Chromium)** to accurately render dynamic client-side web components.
  - Automatically detects video units and switches them to **text format** (`video-or-text` toggle) to capture full lesson transcripts and explanations.
- **Distraction-Free Print Optimization**:
  - Injects custom CSS rules before generating PDFs to remove headers, footers, navigation sidebars, feedback buttons, XP badges, and interactive widgets.
  - Outputs standard **A4 layout** with running title headers and clean margins.
- **Smart Unit Filtering**:
  - Automatically skips quizzes, assessments, and interactive lab environments (*"Knowledge check"*, *"Module assessment"*, *"Exercise - "*), leaving only pure reading content.
- **Consolidated Module PDFs**:
  - Merges individual unit pages into a single complete module PDF using **PdfSharpCore**.
- **Automated Validation**:
  - Validates that every generated document is non-empty (>1 KB), physically valid, and contains readable pages.
- **Structured Output**:
  - Organizes folders and files with zero-padded numbers (`01-...`, `02-...`) for easy sorting.

---

## Output Structure

Generated PDFs are automatically saved in `output/` (or a custom directory specified via `--output`):

```text
output/
└── <Course Title>/
    ├── 01-<Learning Path 1 Title>/
    │   ├── 01-<Module 1 Title>.pdf
    │   ├── 02-<Module 2 Title>.pdf
    │   └── ...
    ├── 02-<Learning Path 2 Title>/
    │   ├── 01-<Module 1 Title>.pdf
    │   └── ...
    └── ...
```

---

## Prerequisites

- **[.NET 10 SDK](https://dotnet.microsoft.com/download)** (or newer)
- **PowerShell** (for installing Playwright browser binaries on Windows)
- Active internet connection

---

## Installation & Setup

1. **Clone the repository**:
   ```bash
   git clone https://github.com/fautaro/microsoft-learn-downloader.git
   cd microsoft-learn-downloader
   ```

2. **Restore dependencies and build**:
   ```bash
   dotnet restore
   dotnet build
   ```

3. **Install Playwright Chromium browser**:
   Run the Playwright install script generated during the build:
   ```powershell
   pwsh ml-downloader/bin/Debug/net10.0/playwright.ps1 install chromium
   ```
   *(On Windows PowerShell, you can also run `powershell ml-downloader/bin/Debug/net10.0/playwright.ps1 install chromium`).*

---

## Usage

### 1. Interactive Mode
Run the tool without arguments. You will be prompted to paste one or more Microsoft Learn URLs:
```bash
dotnet run --project ml-downloader
```
- Paste your URLs (one per line).
- Press **Enter** on an empty line to start downloading.
- If you simply press Enter without typing any URL, the application defaults to an example course (`https://learn.microsoft.com/en-us/training/courses/ai-103t00`).

### 2. Command Line (CLI) Mode
Pass URLs and options directly:

```bash
# Single course
dotnet run --project ml-downloader -- "https://learn.microsoft.com/en-us/training/courses/ai-103t00"

# Specific learning path
dotnet run --project ml-downloader -- "https://learn.microsoft.com/en-us/training/paths/build-copilot-agent/"

# Single module
dotnet run --project ml-downloader -- "https://learn.microsoft.com/en-us/training/modules/prepare-azure-ai-development/"
```

#### Specify Custom Output Directory
Use the `--output` parameter:
```bash
dotnet run --project ml-downloader -- "https://learn.microsoft.com/en-us/training/modules/prepare-azure-ai-development" --output "D:\MyStudyBooks"
```

---

## Supported URL Types

| Type | Example URL |
|:---|:---|
| **Course** | `https://learn.microsoft.com/en-us/training/courses/ai-103t00` |
| **Learning Path** | `https://learn.microsoft.com/en-us/training/paths/develop-ai-solutions-azure-openai/` |
| **Module** | `https://learn.microsoft.com/en-us/training/modules/prepare-azure-ai-development/` |

---

## Tech Stack

- **[C# 13 / .NET 10](https://learn.microsoft.com/en-us/dotnet/)**
- **[Microsoft.Playwright](https://playwright.dev/dotnet/)**: Headless Chromium automation, DOM querying, text format switching, CSS injection, and A4 PDF rendering.
- **[PdfSharpCore](https://github.com/ststeiger/PdfSharpCore)**: Document merging, page management, and structure validation.

---

## Disclaimer

> **Important:** This project is an independent open-source tool developed exclusively for **personal, educational, and offline study purposes**. 
> 
> It is **not** affiliated with, endorsed by, or sponsored by Microsoft Corporation. All Microsoft Learn courses, documentation, content, and trademarks belong solely to Microsoft Corporation. Please respect Microsoft's terms of service when utilizing this tool.

---

## License

This project is licensed under the [MIT License](LICENSE).
