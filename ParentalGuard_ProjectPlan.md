# PARENTAL GUARD — Windows Desktop Application

## Project Plan & Technical Specification

**Version 2.0 | April 2026 | CONFIDENTIAL**

---

## 1. Executive Summary

ParentalGuard is a Windows desktop application that enforces DNS policies, website and application access controls, safe search, usage time limits, and restricted-mode lockdown. The product is architected as a decoupled system: a background enforcement service (Windows Service) and a separate admin UI (WPF), communicating through a PIN-protected, signed configuration file.

This plan prioritizes building the entire enforcement engine and admin UI in C#, deferring the optional WFP kernel callout driver (C) to a separate, independent phase that may never be needed.

### Core Feature Set

- **Private DNS enforcement** — Force all DNS resolution through a configured private resolver; block DoH, DoT, DoQ bypass
- **Website filtering** — Allowlist/blocklist modes with category-based filtering and custom block pages
- **SafeSearch enforcement** — Forced safe search on Google, YouTube, Bing via DNS mapping
- **Application control** — Block, schedule, and time-limit applications by executable hash
- **Restricted Mode (Apps)** — Block all app installations; only pre-approved applications can run
- **Restricted Mode (Web)** — Block all websites except explicitly approved domains
- **Approval workflow** — Controlled user can request parent/supervisor approval for blocked apps/websites via PIN
- **PIN protection** — All configuration changes, mode switches, and uninstall require a supervisor PIN
- **Tamper resistance** — Dual-watchdog services, Safe Mode persistence, registry enforcement

---

## 2. Architecture

### 2.1 Core Principle: Config-File-as-Contract

The UI and Service are fully decoupled. They never communicate directly. The UI writes a signed config.json file; the Service watches it via FileSystemWatcher and applies policy changes. This means the UI can be replaced, restarted, or absent without affecting enforcement.

### 2.2 Technology Stack

| Component | Technology | Rationale |
|---|---|---|
| Enforcement Service | C# / .NET 8 Native AOT | Single binary, no runtime dep, first-class Win32 interop |
| Admin UI | C# / WPF or WinUI 3 | Same language as service, shared class library |
| Shared Library | C# / .NET class library | Config schema, HMAC, SQLite models shared between projects |
| WFP Usermode | C# P/Invoke via CsWin32 | Type-safe bindings auto-generated from Windows metadata |
| Process Monitoring | C# ETW + WMI | Native .NET APIs, no third-party dependency |
| Database | SQLite (Microsoft.Data.Sqlite) | Single-file, zero-config, shared read between UI and service |
| Browser Extension | TypeScript / Manifest V3 | Only non-C# component; required for domain-level time tracking |
| WFP Driver (optional) | C / WDK | Kernel mode requires C; deferred to independent phase |
| Installer | MSIX + WiX bootstrapper | MSIX for Store, WiX for enterprise/sideload |

### 2.3 Solution Structure

```
ParentalGuard.sln
├── src/
│   ├── ParentalGuard.Common/        Config schema, PIN auth, HMAC signing, SQLite models, shared constants
│   ├── ParentalGuard.Service/       Windows Service: DNS, app guard, web filter, SafeSearch, restricted modes, approval queue
│   ├── ParentalGuard.UI/            WPF/WinUI 3 admin panel: config editor, usage dashboard, approval workflow
│   └── ParentalGuard.Installer/     WiX/MSIX packaging project
├── extension/
│   └── src/                         Browser extension (TypeScript, Manifest V3)
└── driver/
    └──                              WFP callout driver (C, separate build, optional)
```

### 2.4 Config-File Contract

The config file uses HMAC-SHA256 signing to prevent tampering by the controlled user. The key is derived from the supervisor PIN via PBKDF2. The service validates the signature and version number (monotonically increasing) before applying changes. On validation failure, the service reverts to last-known-good from an in-memory copy.

```json
{
  "version": 14,
  "updatedAt": "2026-04-04T12:00:00Z",
  "signature": "HMAC-SHA256 of payload",
  "pinHash": "bcrypt hash of supervisor PIN",
  "policy": {
    "dns": {
      "primary": "10.0.0.53",
      "secondary": "10.0.0.54",
      "protocol": "DoH",
      "blockBypassAttempts": true,
      "blockedDoHProviders": ["dns.google", "cloudflare-dns.com", "dns.quad9.net"]
    },
    "webFilter": {
      "mode": "allowlist",
      "restrictedMode": true,
      "allowedDomains": ["*.company.com", "github.com", "wikipedia.org"],
      "blockedDomains": ["*.tiktok.com"],
      "blockedCategories": ["adult", "gambling", "malware", "proxy-vpn"],
      "blockUncategorized": false,
      "customBlockPage": "http://localhost:9443/blocked"
    },
    "appControl": {
      "restrictedMode": true,
      "approvedApps": [
        {"name": "notepad.exe", "sha256": "abc123...", "publisher": "Microsoft"},
        {"name": "code.exe", "sha256": "def456...", "publisher": "Microsoft"}
      ],
      "blockedApps": [
        {"name": "tor.exe", "policy": "blocked"}
      ],
      "blockNewInstallations": true,
      "monitoredInstallerPaths": [
        "C:\\Users\\*\\Downloads",
        "C:\\Users\\*\\Desktop"
      ],
      "rules": [
        {
          "match": {"name": "chrome.exe", "sha256": "..."},
          "policy": "timeLimited",
          "dailyMinutes": 120,
          "schedule": {"allow": "09:00-22:00", "timezone": "Europe/London"}
        }
      ]
    },
    "safeSearch": {
      "google": true,
      "youtube": "moderate",
      "bing": true
    },
    "approvalQueue": {
      "pendingRequests": [
        {
          "id": "req-001",
          "type": "website",
          "value": "stackoverflow.com",
          "requestedBy": "controlled-user",
          "requestedAt": "2026-04-04T10:30:00Z",
          "reason": "Need for homework",
          "status": "pending"
        }
      ],
      "approvedHistory": [],
      "maxPendingRequests": 20
    },
    "pin": {
      "hash": "bcrypt hash",
      "salt": "random salt",
      "failedAttempts": 0,
      "lockoutUntil": null,
      "maxFailedAttempts": 5,
      "lockoutDurationMinutes": 30
    }
  }
}
```

### 2.5 PIN Security Model

The supervisor PIN is the single authentication mechanism protecting all privileged operations. It is never stored in plaintext.

**PIN-protected operations:**
- All config changes (DNS, web filter, app control, SafeSearch, restricted mode toggles)
- Approving or rejecting requests from controlled user
- Switching between restricted and standard modes
- Overriding uninstall protection
- Pausing enforcement temporarily
- Viewing/exporting usage reports
- Changing the PIN itself (requires current PIN)

**PIN storage:** bcrypt hash stored in config.json and independently in a DPAPI-encrypted registry key (backup if config is deleted).

**Brute-force protection:** 5 failed attempts → 30-minute lockout. Lockout state stored in service memory (not config file, so user can't reset by editing file). After lockout, exponential backoff: 30 min → 1 hr → 2 hr → 4 hr.

**PIN recovery:** Optional recovery key generated at first-run setup (32-character alphanumeric). Displayed once, user must write it down. Stored as bcrypt hash. This is the only way to recover from a forgotten PIN.

---

## 3. New Feature Specifications

### 3.1 Restricted Mode: Application Lockdown

**Goal:** Prevent installation of any new software and allow only pre-approved applications to execute.

**Installation blocking mechanisms:**

1. **MSI/MSIX installer interception:** Monitor `msiexec.exe` and `Add-AppxPackage` process creation via ETW. Terminate immediately unless the installer hash is in the approved list.
2. **Executable monitoring in download paths:** FileSystemWatcher on Downloads, Desktop, and Temp directories for new `.exe`, `.msi`, `.msix`, `.appx`, `.bat`, `.cmd`, `.ps1`, `.vbs` files. Quarantine (move to a hidden, ACL-protected directory) on detection.
3. **AppLocker integration (optional):** On Windows Enterprise/Education, programmatically configure AppLocker policies via `IAppIdPolicyHandler` COM interface to enforce an executable allowlist at the OS level.
4. **Program Files monitoring:** WMI event subscription on `Win32_Product` for new installations. If detected and not approved, trigger alert and log.

**Approved app enforcement:**

- Every process creation event (ETW) is checked against the approved app list
- Match by SHA-256 hash of the executable (primary) + publisher certificate (secondary)
- Unknown executables are terminated immediately with a toast notification explaining the block
- Windows system processes (svchost, csrss, explorer, etc.) are auto-approved via a hardcoded whitelist of Microsoft-signed system binaries

**Config toggle:** `appControl.restrictedMode: true/false` — when false, only explicit block rules apply (standard mode).

### 3.2 Restricted Mode: Website Lockdown

**Goal:** Block all web traffic except to explicitly approved domains.

This is the existing allowlist mode from Phase 2, elevated to a named "Restricted Mode" with additional hardening:

- `webFilter.restrictedMode: true` activates strict allowlist enforcement
- DNS proxy returns NXDOMAIN for ANY domain not in `allowedDomains`
- Wildcard support: `*.wikipedia.org` allows all subdomains
- Automatic inclusions when restricted mode is on: Windows Update domains (to prevent OS update failures), CRL/OCSP domains (certificate validation), configured private DNS resolver domain
- Block page clearly indicates restricted mode is active and provides the "Request Approval" button

### 3.3 Approval Workflow

**Goal:** Allow the controlled user to request access to a blocked website or application, with the supervisor approving/rejecting via PIN.

**User-facing flow (no PIN required):**

1. User encounters a blocked website → block page shows "Request Access" button
2. User clicks → enters the domain (pre-filled) and an optional reason
3. Request is written to `approvalQueue.pendingRequests` in config (this write does NOT require PIN — it's the only unsigned section, validated separately by the service)
4. Toast notification sent to supervisor (if logged in) or queued for next admin UI launch

**User-facing flow for apps:**

1. User tries to launch a blocked app → toast notification with "Request Access" button
2. Clicking opens a minimal request dialog (not the admin panel) — domain/app name + reason field
3. Request queued same as above

**Supervisor-facing flow (PIN required):**

1. Admin UI shows approval queue with pending requests (badge count on system tray icon)
2. Supervisor enters PIN to access the approval panel
3. Per request: Approve (permanently add to allowlist), Approve Temporarily (1hr / 1day / 1week), or Reject
4. Approved domains/apps are added to the appropriate allowlist in config
5. Temporary approvals include an `expiresAt` timestamp; service auto-removes when expired

**Approval request schema:**

```json
{
  "id": "req-001",
  "type": "website | app",
  "value": "stackoverflow.com | notepad++.exe",
  "sha256": null,
  "requestedBy": "controlled-user",
  "requestedAt": "2026-04-04T10:30:00Z",
  "reason": "Need for school project",
  "status": "pending | approved | approved-temp | rejected",
  "approvedAt": null,
  "expiresAt": null,
  "approvedBy": "supervisor"
}
```

**Limits:** Max 20 pending requests (configurable). Duplicate requests for the same domain/app within 24 hours are silently dropped. Rate limit: max 5 requests per hour from controlled user.

### 3.4 PIN-Protected Configuration

**Every write to config.json requires PIN validation.** The flow:

1. Supervisor opens admin UI → enters PIN at login screen
2. PIN validated against bcrypt hash (in-memory session, configurable timeout: default 15 minutes)
3. Session active → config changes are allowed; each save re-signs config with HMAC
4. Session expires → PIN re-entry required
5. Service validates HMAC on every config file change; unsigned/invalid changes are rejected and reverted

**PIN change flow:** Current PIN → New PIN → Confirm New PIN. Service updates both config hash and DPAPI registry backup.

**Uninstall override:** When uninstall is attempted (detected via registry watch on `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`), a modal dialog appears requiring the supervisor PIN. Without correct PIN, the uninstall is blocked and the service re-registers itself if any service entries were modified.

---

## 4. Phased Delivery Plan

### Phase Overview

| Phase | Focus | Duration | Language | Key Deliverable |
|---|---|---|---|---|
| Phase 1 | DNS Enforcement + Foundation + PIN System | 7 weeks | C# only | DNS locked to private resolver, PIN auth system |
| Phase 2 | Web Filtering + SafeSearch + Restricted Web Mode | 6 weeks | C# only | Domain allow/blocklist, category filter, restricted mode, SafeSearch |
| Phase 3 | App Control + Restricted App Mode + Approval Workflow | 7 weeks | C# + TS | App blocking, installation blocking, approval queue, browser extension |
| Phase 4 | Hardening + Polish | 4 weeks | C# only | Tamper protection, AV whitelisting, installer, UX polish |
| Phase 5 | WFP Kernel Driver (Optional) | 4–6 weeks | C only | Deep packet inspection, SNI rewrite (only if needed) |

### 4.1 Phase 1: DNS Enforcement + Foundation + PIN System (7 Weeks)

**Goal:** Lock all DNS resolution to a configured private resolver, prevent bypass, establish the service architecture, config contract, and PIN authentication system.

**Week 1–2: Service Skeleton + Config System + PIN Auth**

1. Create .NET 8 solution with Common, Service, and UI projects
2. Implement BackgroundService with Microsoft.Extensions.Hosting
3. Define config.json schema in Common library (DNS settings, version, HMAC signature, PIN hash, approval queue)
4. Implement PIN system in Common library: bcrypt hashing, PBKDF2 key derivation for HMAC, brute-force lockout logic
5. Implement PIN storage: bcrypt hash in config + DPAPI-encrypted backup in registry
6. Implement recovery key generation (32-char alphanumeric) and bcrypt hash storage
7. Implement HMAC-SHA256 signing and validation with PIN-derived key
8. Implement FileSystemWatcher in Service to monitor config changes
9. Implement atomic write (write-to-temp, rename) in UI config writer
10. Set up SQLite database schema for usage logging and approval history

**Week 3–4: DNS Lock + WFP Usermode Filters**

1. Integrate CsWin32 source generator for WFP API bindings (fwpuclnt.dll)
2. Set system DNS to configured private resolver via WMI (Win32_NetworkAdapterConfiguration)
3. Add WFP usermode filter: block all outbound UDP/53 except to private resolver IP
4. Add WFP usermode filter: block all outbound TCP/53 except to private resolver IP
5. Add WFP usermode filter: block TCP/853 (DoT) to all destinations
6. Build and maintain DoH provider IP blocklist (Cloudflare, Google, Quad9, Mozilla, NextDNS)
7. Add WFP filter: block TCP/443 to known DoH provider IPs
8. Add WFP filter: block UDP/443 and UDP/8853 to known DoQ provider IPs
9. WMI event subscription to monitor adapter DNS changes; auto-revert on tamper

**Week 5: Browser DoH Override + Registry Enforcement**

1. Set Chrome registry policy: DnsOverHttpsMode = off (HKLM\SOFTWARE\Policies\Google\Chrome)
2. Set Edge registry policy: DnsOverHttpsMode = off (HKLM\SOFTWARE\Policies\Microsoft\Edge)
3. Set Firefox registry policy: DNSOverHTTPS Enabled=false, Locked=true
4. Add canary domain sinkhole: return NXDOMAIN for use-application-dns.net via private DNS or local resolver
5. WMI registry event watcher: monitor policy keys, auto-revert if changed
6. Add Brave, Vivaldi, Opera, Arc policy keys (all Chromium-based, same pattern)

**Week 6–7: Admin UI Foundation + PIN UI + Testing**

1. Build WPF/WinUI 3 admin panel with PIN setup wizard (first-run) and PIN login screen
2. PIN session management: configurable timeout (default 15 min), re-entry required after timeout
3. PIN change screen: current PIN → new PIN → confirm
4. Recovery key display (first-run only, one-time, must-acknowledge)
5. DNS configuration screen (PIN-protected): primary/secondary resolver, protocol selection
6. Service status dashboard: running/stopped, current DNS config, active WFP filter count
7. Register service with sc create, test start/stop/recovery
8. Manual DNS bypass test suite: verify direct IP DNS queries are blocked, DoH/DoT/DoQ blocked
9. Integration test: change DNS in Windows settings, verify service reverts within 3 seconds
10. PIN brute-force test: verify lockout after 5 failed attempts, exponential backoff
11. Package as MSIX for internal testing

**Phase 1 Exit Criteria:**

- All DNS traffic routes through configured private resolver
- DoH, DoT, DoQ bypass attempts are blocked for all major browsers
- Browser registry policies are enforced and tamper-resistant
- PIN authentication works with lockout and recovery
- Admin UI requires PIN for all configuration access
- Config file HMAC validation prevents unsigned modifications

---

### 4.2 Phase 2: Web Filtering + SafeSearch + Restricted Web Mode (6 Weeks)

**Goal:** Block harmful websites by domain, enforce SafeSearch, provide category-based filtering, and implement restricted web mode (allowlist-only).

**Week 8–9: Domain Filtering Engine**

1. Implement local DNS proxy listener on 127.0.0.1:53 using UdpClient (C#)
2. DNS packet parser: extract query domain from question section (byte 12+, label format)
3. Blocklist engine: in-memory HashSet with wildcard support (*.tiktok.com matches sub.tiktok.com)
4. Allowlist mode: reject any domain not in whitelist with NXDOMAIN response
5. Blocklist mode: reject matched domains, forward all others upstream
6. Sinkhole response: return configurable IP (127.0.0.1) for blocked domains, redirect to custom block page
7. Update WFP filters to force all DNS to 127.0.0.1:53 (local proxy)
8. Upstream forwarding: relay clean queries to private DNS resolver and cache responses (TTL cache)

**Week 10: Category-Based Filtering + Restricted Web Mode**

1. Integrate domain categorization database (UT1 blocklists or Shallalist, free, ~2M domains)
2. Category parser: load category files into per-category HashSets on service start
3. Config schema extension: blockedCategories array (adult, gambling, malware, proxy-vpn, social-media, etc.)
4. Auto-update mechanism: daily download of updated blocklists via HTTPS, atomic swap
5. Restricted Web Mode implementation: when `webFilter.restrictedMode = true`, DNS proxy switches to strict allowlist — only `allowedDomains` resolve, everything else returns NXDOMAIN
6. Auto-include essential domains in restricted mode: Windows Update (*.windowsupdate.com, *.microsoft.com/updates), CRL/OCSP endpoints, private DNS resolver hostname
7. Custom domain override: admin can allowlist a domain even if its category is blocked

**Week 11: SafeSearch Enforcement**

1. Google SafeSearch: DNS-map www.google.com to forcesafesearch.google.com (216.239.38.120)
2. YouTube Restricted: DNS-map youtube.com to restrictmoderate.youtube.com
3. Bing Strict: DNS-map www.bing.com to strict.bing.com
4. DuckDuckGo: block entirely unless safe=1 enforcement is feasible (add to blocklist by default)
5. Block unconfigurable search engines: add Yandex, Baidu, etc. to blocklist
6. All mappings implemented in local DNS proxy, configurable via config.json

**Week 12–13: Block Page + Approval Request UI + Testing**

1. Custom block page: local HTTP server on 127.0.0.1:9443 serving branded HTML
2. Block page features: shows blocked domain, reason (category/restricted mode/explicit block), "Request Access" button
3. Request Access flow on block page: pre-filled domain, optional reason text field, submit → writes to approval queue in config
4. Approval queue write validation: service accepts unsigned approval requests but validates structure, deduplicates, enforces rate limit (5/hr) and max pending (20)
5. Admin UI: domain allowlist/blocklist editor with import/export (CSV), PIN-protected
6. Admin UI: category selection screen with toggle per category, domain count per category
7. Admin UI: SafeSearch toggle per search engine
8. Admin UI: restricted web mode toggle (prominent, with confirmation dialog)
9. Admin UI: real-time blocked request log viewer (tail SQLite log table)
10. Test matrix: verify blocking across Chrome, Edge, Firefox, Brave in both modes
11. Performance benchmark: DNS proxy latency (<5ms local, <50ms upstream)

**Phase 2 Exit Criteria:**

- Domain-level blocking works in both allowlist and blocklist modes
- Restricted Web Mode blocks all sites except approved domains
- Category-based filtering operational with auto-updating blocklists
- SafeSearch enforced on Google, YouTube, Bing
- Block page shows "Request Access" button; requests queue correctly
- Custom block page displayed for blocked sites
- All web filter config changes require PIN

---

### 4.3 Phase 3: App Control + Restricted App Mode + Approval Workflow (7 Weeks)

**Goal:** Monitor and control application usage with blocking, scheduling, time quotas, restricted mode (block all unapproved apps and installations), and a full approval workflow for both apps and websites.

**Week 14–15: Application Monitoring + Blocking + Restricted Mode**

1. ETW consumer: subscribe to Microsoft-Windows-Kernel-Process provider for real-time process creation events
2. Process fingerprinting: compute SHA-256 hash of executable on launch
3. Blocked app enforcement: terminate process immediately on launch if matched by hash or publisher certificate
4. **Restricted App Mode:** when `appControl.restrictedMode = true`, every process creation is checked against `approvedApps` list — unknown processes terminated immediately
5. System process whitelist: auto-approve Microsoft-signed system binaries (svchost, csrss, explorer, dwm, taskmgr, etc.) — verified by Authenticode signature, not just filename
6. **Installation blocking:** monitor process creation for `msiexec.exe`, `setup.exe`, `install*.exe`, `Add-AppxPackage`; terminate unless installer hash is in approved list
7. **Download path monitoring:** FileSystemWatcher on `C:\Users\*\Downloads`, Desktop, Temp for new .exe/.msi/.msix/.appx/.bat/.cmd/.ps1/.vbs files; quarantine to ACL-protected hidden directory
8. Toast notification on block: "[App Name] was blocked. Tap to request access."
9. Clicking toast opens minimal request dialog (not admin panel): app name, optional reason, submit

**Week 16–17: Time Limits + Scheduling**

1. Scheduled access: allow/block based on time-of-day rules (e.g., Chrome only 09:00–22:00)
2. Foreground time tracker: poll GetForegroundWindow at 1-second intervals, map to process, accumulate daily time
3. Time quota enforcement: when daily limit reached, terminate process and block for remainder of day
4. Grace period warning: 5-minute and 1-minute toast notifications before quota expires (Windows Notification API)
5. Midnight reset: reset all daily counters at configured reset time (default 00:00, configurable timezone)

**Week 18–19: Browser Extension + Domain Time Limits**

1. Build Manifest V3 Chrome/Edge extension: reports active tab URL to native messaging host
2. Native messaging host in C#: receives domain from extension, updates domain time accumulator in service
3. Per-domain daily time limit enforcement: when limit hit, extension redirects tab to block page
4. Extension tamper detection: service monitors extension installation via registry (ExtensionInstallForcelist)
5. Extension removal response: if extension uninstalled, kill the browser process
6. Firefox WebExtension port (same logic, different manifest and native messaging setup)
7. Fallback: SNI-based domain tracking via WFP connection logging (less accurate, no extension needed)

**Week 20: Approval Workflow + Admin UI + Dashboard**

1. **Full approval workflow implementation:**
   - Service processes approval queue: validates requests, deduplicates, enforces rate limits
   - Supervisor notification: system tray badge count for pending requests; toast notification on new request
   - Admin UI approval panel (PIN-protected): list pending requests with Approve / Approve Temporarily (1hr/1day/1week) / Reject buttons
   - Approve action: add domain/app to appropriate allowlist in config, sign with HMAC
   - Temporary approval: `expiresAt` timestamp; service auto-removes and re-blocks when expired
   - Rejection: update status, optional rejection reason displayed to controlled user via toast
   - Approval history log in SQLite: who requested, what, when, outcome, expiry
2. App control screen (PIN-protected): add/remove approved apps, set time limits, configure schedules
3. App discovery: scan Program Files and Start Menu for installed applications, present as selectable list for approval
4. Restricted mode toggles (PIN-protected, with confirmation dialogs): app restricted mode, web restricted mode
5. Website time limits screen (PIN-protected): per-domain quota editor
6. Usage dashboard: daily/weekly charts of app and website usage time (LiveCharts2)
7. Activity log: searchable, filterable log of all blocks, approvals, and policy changes
8. Export reports (PIN-protected): generate PDF/CSV usage reports for a date range

**Phase 3 Exit Criteria:**

- Restricted App Mode blocks all unapproved applications and new installations
- Approval workflow works end-to-end: request → notify → approve/reject → enforce
- Temporary approvals auto-expire correctly
- Applications can be blocked, scheduled, and time-limited
- Browser extension tracks per-domain time and enforces limits
- Usage dashboard shows accurate daily/weekly statistics
- Extension tamper detection operational
- All app control and approval config changes require PIN

---

### 4.4 Phase 4: Hardening + Polish (4 Weeks)

**Goal:** Make the product tamper-resistant, AV-friendly, installable, and production-ready.

**Week 21–22: Tamper Protection**

1. Service self-protection: set service DACL to deny stop/pause to non-administrator accounts
2. Watchdog service: secondary lightweight service that monitors primary service heartbeat; restarts if killed
3. Cross-watchdog: primary service also monitors watchdog; each restarts the other
4. Safe Mode persistence: register both services for SafeBoot startup via registry
5. Config file protection: if config.json deleted or corrupted, service falls back to last-known-good (in-memory) and recreates file
6. **Uninstall protection with PIN override:**
   - Monitor `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall` for changes targeting ParentalGuard
   - On uninstall attempt: intercept and display modal dialog requiring supervisor PIN
   - Correct PIN → allow uninstall to proceed, gracefully stop services, remove WFP filters
   - Incorrect PIN or dismissal → block uninstall, re-register services if tampered, log the attempt
   - PIN lockout applies (5 attempts → 30 min lockout with exponential backoff)
7. Block known bypass tools: add VPN clients, Tor, proxy tools to default blocklist; block non-standard outbound ports

**Week 23: Code Signing + AV Whitelisting**

1. Obtain EV code-signing certificate (DigiCert, Sectigo; $200–$400/year)
2. Sign all binaries: service exe, UI exe, watchdog exe, installer, native messaging host
3. Submit to Microsoft Defender for whitelisting (Security Intelligence portal)
4. Submit to major AV vendors: Norton, Kaspersky, Bitdefender, Avast false-positive reporting portals
5. SmartScreen reputation building: signed MSIX distributed via Microsoft Store for fastest reputation

**Week 24: Installer + UX Polish**

1. WiX bootstrapper: installs .NET 8 runtime (if not AOT), registers services, installs browser extension, sets registry policies
2. **First-run wizard:**
   - Supervisor PIN creation (6–8 digit numeric or alphanumeric, configurable)
   - Recovery key generation + display (must-acknowledge, one-time)
   - DNS configuration
   - Initial mode selection (standard vs restricted)
   - Initial blocklist category selection
   - Approved app auto-detection (scan installed apps, present for bulk approval)
3. MSIX package for Microsoft Store distribution (optional channel)
4. System tray icon: quick status view, pending approval badge, open admin panel, pause enforcement (requires PIN)
5. Auto-update mechanism: check for updates on service start and daily; download and apply via installer
6. Localization infrastructure: resource files for English (launch language), structure for future languages
7. End-to-end QA pass: test all features on Windows 10 (21H2+) and Windows 11 (22H2+)

**Phase 4 Exit Criteria:**

- Service survives user attempts to stop, uninstall, or tamper
- Uninstall requires PIN; incorrect PIN blocks uninstall
- No false positives from Windows Defender or top 5 AV engines
- Clean install experience with first-run wizard via MSIX or WiX installer
- Product is distributable and production-ready

---

### 4.5 Phase 5: WFP Kernel Driver — Optional, C Only (4–6 Weeks)

**This phase is entirely separate from the C# codebase. It is only needed if usermode WFP filters prove insufficient for deep packet inspection or transparent DNS rewriting.**

**Prerequisites (Before Starting):**

- EV code-signing certificate (already obtained in Phase 4)
- Windows Hardware Dev Center account ($19 one-time)
- WDK (Windows Driver Kit) installed alongside Visual Studio
- Test-signing enabled on a dedicated test machine
- Validated that usermode WFP is genuinely insufficient for a specific use case

**Week 25–26: Driver Development**

1. Minimal WFP callout driver in C: register callout at FWPM_LAYER_OUTBOUND_TRANSPORT_V4
2. Classify function: inspect UDP/53 packets, extract queried domain from DNS payload
3. Block/permit decision: check domain against policy (communicated from C# service via shared memory or inverted I/O call)
4. Optional: rewrite DNS server IP in outbound packet for transparent redirect
5. BSOD-safe coding: no allocations in classify path, IRQL-safe operations only

**Week 27–28: Signing + Integration**

1. WHQL attestation signing submission via Hardware Dev Center
2. Integration with C# service: service loads/unloads driver via SCM, communicates policy via IOCTL or shared memory
3. Fallback mechanism: if driver fails to load (unsigned, incompatible Windows build), service falls back to usermode filters automatically
4. Stress testing: 1000+ DNS queries/sec, verify no leaks, no BSODs, no performance degradation
5. Windows Update compatibility testing across latest Windows 10 and 11 builds

**Phase 5 Exit Criteria:**

- Driver signed via WHQL attestation and loads on stock Windows without test-signing
- Service gracefully falls back to usermode if driver unavailable
- No BSODs or memory leaks under sustained load

---

## 5. Summary Timeline

| Phase | Name | Weeks | Language | People | Cumulative |
|---|---|---|---|---|---|
| 1 | DNS Enforcement + PIN System | 1–7 | C# | 1–2 | Week 7 |
| 2 | Web Filtering + Restricted Web | 8–13 | C# | 1–2 | Week 13 |
| 3 | App Control + Restricted Apps + Approvals | 14–20 | C# + TS | 2–3 | Week 20 |
| 4 | Hardening + Polish | 21–24 | C# | 1–2 | Week 24 |
| 5 | Kernel Driver (optional) | 25–30 | C | 1 | Week 30 (if needed) |

**Total C# delivery: 24 weeks.** Product is fully functional and shippable after Phase 4 without any C code.

---

## 6. Risk Register

| Risk | Severity | Impact | Mitigation | Phase |
|---|---|---|---|---|
| AV false positives | HIGH | Product flagged as malware, blocks distribution entirely | EV cert + AV vendor submissions + Store distribution | Validate in Phase 1, resolve in Phase 4 |
| SmartScreen blocking | HIGH | Users see scary warning on install, abandonment | EV code-signing from day one, reputation building via Store | Phase 4 |
| Browser DoH evolution | MED | Browsers add new DoH providers or change protocol, bypassing blocks | Auto-updating DoH provider IP list, registry policy enforcement | Ongoing maintenance |
| Manifest V3 restrictions | MED | Chrome further restricts native messaging, breaks extension | SNI-based domain tracking as fallback (no extension needed) | Phase 3 |
| Determined user bypass | MED | User boots USB, uses portable VPN, or finds novel bypass | Document threat model honestly; block known tools; BIOS password recommendation | Phase 4 hardening |
| WHQL signing delays | LOW | Driver signing takes weeks, blocks Phase 5 delivery | Submit stub driver early to validate pipeline; Phase 5 is optional | Phase 5 only |
| WFP API complexity | MED | CsWin32 bindings for WFP are verbose, few C# examples exist | Spike WFP filter creation in Week 1; validate P/Invoke approach early | Phase 1, Week 3 |
| PIN recovery failure | MED | Supervisor forgets PIN and loses recovery key; system locked | Clear first-run UX emphasizing recovery key importance; optional email backup of recovery key | Phase 1 |
| Approval queue abuse | LOW | Controlled user spams approval requests | Rate limiting (5/hr), max pending (20), duplicate detection | Phase 3 |
| Restricted mode over-blocking | MED | Essential Windows services break when restricted mode blocks system domains | Auto-include Microsoft/Windows Update/CRL domains; tested whitelist of system-critical domains | Phase 2–3 |

---

## 7. Key Technical Decisions

| Decision | Chosen | Rationale |
|---|---|---|
| Primary language | C# / .NET 8 Native AOT | Single language for 90%+ codebase; best Win32 interop; largest talent pool |
| UI framework | WPF (or WinUI 3) | Same language as service; shared class library; no additional runtime |
| UI/Service communication | Signed config file (not IPC) | Full decoupling; UI can be absent; tamper-evident; simple to debug |
| Authentication mechanism | PIN (not password) | Simpler UX for the use case; sufficient entropy for local-only auth; faster to enter |
| DNS filtering approach | Local DNS proxy in C# | Full control over resolution; no external dependency; works offline |
| WFP approach | Usermode filters via CsWin32 P/Invoke | No driver needed for block/allow decisions; covers 95% of use cases |
| Kernel driver | Deferred to optional Phase 5 | Only needed for packet rewriting; usermode sufficient for MVP through production |
| Tauri vs WPF for UI | WPF chosen over Tauri | Eliminates Rust dependency; single-language stack; simpler build pipeline |
| Approval queue storage | In config.json (pending) + SQLite (history) | Pending requests travel with config; history is queryable for dashboard |
| Restricted mode approach | Service-enforced, not OS-level | Works on all Windows editions; no Group Policy dependency; simpler implementation |

---

## 8. External Dependencies

- **EV code-signing certificate** — required before any public distribution (order in Week 1)
- **Windows Hardware Dev Center account** — only needed if Phase 5 is pursued
- **Domain categorization database (UT1/Shallalist)** — free, auto-updated, integrated in Phase 2
- **Microsoft Defender whitelisting submission** — submit after Phase 1 binary is stable
- **AV vendor false-positive portals** — submit during Phase 4
- **Microsoft Store developer account ($19)** — optional distribution channel

---

## 9. Success Metrics

| Metric | Target | Measurement |
|---|---|---|
| DNS bypass rate | 0% | Automated test suite: attempt DoH/DoT/DoQ/direct DNS |
| DNS proxy latency (local) | <5ms p99 | Benchmark 10K queries, measure response time |
| DNS proxy latency (upstream) | <50ms p99 | Benchmark forwarded queries to upstream resolver |
| AV detection rate | 0 detections in top 10 AV | VirusTotal scan after each release |
| Service memory footprint | <30MB working set | Task Manager after 24hr runtime |
| Config change application time | <3 seconds | Write config from UI, measure time until service applies |
| Tamper recovery time | <5 seconds | Kill service, measure watchdog restart time |
| PIN validation latency | <200ms | Bcrypt verify with cost factor 12 |
| Approval request to notification | <5 seconds | Submit request, measure time to supervisor toast |
| Restricted mode false-block rate | <1% of system processes | Run in restricted mode for 24hr, count incorrect blocks on system processes |
