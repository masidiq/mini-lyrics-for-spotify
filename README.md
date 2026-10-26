# Mini Lyrics for Spotify

Aplikasi Windows dengan lirik mengambang, teks kuning, auto-scroll, fade atas/bawah, dan mode tembus klik. Mengikuti Spotify desktop melalui Windows Media Sessions, tanpa login, cookie, atau Spotify Web API.

[Download EXE Windows x64](https://github.com/masidiq/mini-lyrics-for-spotify/releases/latest) · [Changelog](CHANGELOG.md)

## Screenshot

Mode teks transparan dengan auto-scroll dan fade:

![Mode teks dengan auto-scroll dan fade](docs/screenshots/auto-scroll-fade.png)

Panel biasa:

![Panel Mini Lyrics](docs/screenshots/panel.png)

Pengaturan:

![Pengaturan Mini Lyrics](docs/screenshots/settings.png)

Screenshot menggunakan teks demo, bukan lirik lagu atau tampilan desktop pengguna. Background gambar mode teks transparan.

## Jalankan

1. Unduh `MiniLyricsForSpotify-v0.1.0-win-x64.exe` dari [GitHub Releases](https://github.com/masidiq/mini-lyrics-for-spotify/releases/latest), lalu jalankan. Keluar dari versi lama lewat menu tray **Keluar** terlebih dahulu jika masih berjalan.
2. Buka Spotify **desktop di komputer yang sama**, lalu putar lagu.
3. Tunggu judul dan lirik muncul. Pencarian awal memerlukan internet; lirik yang sudah tersimpan dapat dimuat dari cache.

EXE release **portable, single-file, dan self-contained Windows x64** menyertakan runtime .NET 10.0.6. Tidak perlu memasang .NET atau menyalin DLL terpisah. Target minimum: Windows 10 versi 2004/build 19041; build dan pemeriksaan runtime dilakukan pada Windows 11. EXE belum ditandatangani secara digital.

Release memuat `CHANGELOG.md`, `verification.log`, `smoke-test.json`, dan `SHA256SUMS.txt`. Periksa checksum EXE yang diunduh dengan `Get-FileHash .\MiniLyricsForSpotify-v0.1.0-win-x64.exe -Algorithm SHA256`, lalu bandingkan dengan `SHA256SUMS.txt` dari release yang sama.

## Kontrol

- Geser header untuk memindahkan panel; tarik tepi/sudut untuk mengubah ukuran. Ukuran awal 420 × 220.
- Seluruh area kosong header dan area judul/artis menerima drag; tidak harus tepat mengenai teks atau logo.
- Aplikasi langsung membuka **mode teks saja**: beberapa baris lirik kuning dengan shadow hitam, tanpa background, header, tombol, atau resize grip. Baris aktif terlihat terang di tengah; baris sekitarnya lebih redup, dan tepi atas/bawah memudar sampai transparan. Lirik bertimestamp otomatis bergulir dengan transisi 400 ms; seek ke baris jauh langsung memusatkan ulang. Pengaturan lama dimigrasikan satu kali ke mode ini. Geser teks untuk memindahkannya. Lirik biasa tanpa timestamp digulir manual menggunakan mouse wheel.
- Hilangkan centang **Mode teks saja** untuk kembali ke panel. Pengaturan dan coba ambil lirik lagi juga tersedia di menu tray. Membuka pengaturan dari mode teks menampilkan panel sementara; **Selesai** mengembalikan tampilan teks saja. Pilihan mode tersimpan saat restart.
- **Tembus klik (abaikan mouse)** aktif secara default: klik kiri/kanan, drag, dan scroll melewati seluruh window lirik ke aplikasi di belakangnya. Auto-scroll tetap berjalan. Untuk memindahkan lirik atau menggulir manual, klik kanan ikon tray dekat jam dan hilangkan centang mode ini. Pilihan tersimpan saat restart. Menu tray tetap menerima klik; membuka **Pengaturan** menonaktifkan tembus klik sementara sampai menekan **Selesai**.
- Tombol roda gigi membuka pengaturan: ukuran lirik 14–36 px, opasitas 35–100%, always-on-top, dan koreksi waktu −10 hingga +10 detik.
- Koreksi positif **memajukan** lirik; koreksi negatif **menunda** lirik. Panel pengaturan memperbesar tinggi sementara, lalu kembali ke ukuran sebelumnya.
- Tombol minimize, tutup, dan Alt+F4 menyembunyikan panel ke system tray.
- Klik dua kali ikon tray untuk menampilkan panel. Menu kanan menyediakan tampilkan/sembunyikan dan **Keluar** untuk menghentikan aplikasi.
- Lirik bertimestamp menyorot satu baris mengikuti playback; lirik biasa ditampilkan sebagai teks yang dapat digulir. Sinkronisasi mengikuti play, pause, seek, dan pergantian lagu.
- Jika koneksi gagal atau lirik tidak ditemukan, gunakan **Coba lagi**. Tidak ada retry jaringan tanpa batas.

## Penyimpanan dan privasi

Data lokal berada di `%LocalAppData%\MiniLyricsForSpotify`:

- `settings.json`: posisi/ukuran panel dan pengaturan tampilan; disimpan otomatis setelah perubahan atau saat panel ditutup.
- `cache/*.json`: hasil LRCLIB dengan nama berbasis hash identitas lagu. Cache rusak diambil ulang; kegagalan menulis cache tidak menghalangi tampilan lirik.

Untuk mencari lirik, judul, artis, album, dan durasi dikirim ke `https://lrclib.net/api/get`. Tidak ada audio atau token Spotify yang dikirim. Hanya sesi dengan ID Spotify desktop yang dipilih; browser dan aplikasi musik lain diabaikan. Aplikasi tidak mengontrol playback dan tidak mengunduh audio.

## Build dan verifikasi

SDK pengembangan dipatok ke .NET SDK 10.0.202 (latest patch diperbolehkan), dengan Windows SDK projection `Microsoft.Windows.SDK.NET.Ref` versi `10.0.22621.57`. WPF, WinForms untuk tray, dan HttpClient menggunakan framework bawaan. Tidak memakai dependency UI atau framework pengujian pihak ketiga.

`NuGet.Config` menonaktifkan semua feed agar build memakai cache lokal tanpa unduhan otomatis. Pada komputer baru, isi cache secara eksplisit terlebih dahulu:

```powershell
dotnet restore MiniLyrics.slnx --source https://api.nuget.org/v3/index.json -r win-x64 -p:SelfContained=true
```

Jalankan dari folder proyek menggunakan PowerShell:

```powershell
dotnet build MiniLyrics.slnx -c Release -p:NuGetAudit=false
dotnet run --project tests/MiniLyrics.Tests -c Release -p:NuGetAudit=false
dotnet publish src/MiniLyrics.App/MiniLyrics.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableSingleFileAnalyzer=false -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:NuGetAudit=false -o artifacts/publish
```

Pengujian memakai console harness dengan exit code nonzero jika assertion gagal; jalankan dengan `dotnet run`, bukan `dotnet test`. Tes mencakup parser LRC, koreksi waktu, jam playback, cancel/race antar-lagu, cache, encoding query, HTTP error, instrumental, dan fallback lirik biasa. HTTP provider pada tes ini memakai fake handler dan tidak menghubungi jaringan.

Smoke test aplikasi publish:

```powershell
$app = Join-Path $PWD 'artifacts/publish/MiniLyricsForSpotify.exe'
$output = Join-Path $PWD 'artifacts/verification'
Start-Process -FilePath $app -ArgumentList @('--smoke-test', $output) -WindowStyle Hidden -Wait
Get-Content (Join-Path $output 'smoke-test.json')
```

Smoke test menulis hasil JSON dan screenshot `waiting.png`, `lyrics.png`, serta `settings.png`; lalu keluar otomatis. Teks lirik pada screenshot adalah teks sampel asli untuk pemeriksaan UI. Test tidak mengambil lirik dari jaringan dan tidak menyimpan perubahan ke pengaturan pengguna. `passed` hanya mencakup pemeriksaan lokal; lihat `spotifyIntegration` secara terpisah.

Pemeriksaan tambahan mencakup hit-test header kosong, teks kuning/shadow, background transparan, baris aktif di tengah, auto-scroll, seek mundur, tembus klik native Windows, kembali ke panel, dan persistensi pengaturan. Screenshot tambahan: `auto-scroll-fade.png` dan `text-only.png`. Hasil verifikasi EXE release tersedia pada asset `verification.log` dan `smoke-test.json` di GitHub Releases.

### Hasil verifikasi 26 Oktober 2026

- Build/publish Release x64: berhasil, **0 warning, 0 error**, seluruh dependency berasal dari cache lokal.
- Pengujian inti: **28/28 lulus**.
- Smoke test paket self-contained: ukuran awal, always-on-top, tray, close-to-tray, restore, pengaturan, sorotan baris, kembali ke ukuran ringkas, simpan/baca pengaturan, pemulihan JSON rusak, dan filter Spotify **lulus**. Screenshot diperiksa secara visual.
- Panggilan LRCLIB langsung untuk metadata sampel: **HTTP 200**, tersedia lirik biasa dan bertimestamp. Isi lirik tidak dimasukkan ke dokumentasi.
- Pemeriksaan versi kedua: sesi Spotify desktop **terdeteksi**. Mode teks, shadow/warna, alpha background, kembali ke panel, area header untuk drag, dan persistensi mode **lulus**. Sinkronisasi lagu sungguhan, pause/seek langsung, drag dengan mouse fisik, serta penggunaan saat aplikasi lain aktif masih memerlukan pemeriksaan manual. Pengujian clock dengan data buatan sudah lulus.

Checklist manual lanjutan: buka aplikasi dan Spotify, putar lagu, pause/resume, seek maju/mundur, ganti lagu cepat, cek teks biasa/instrumental bila tersedia, pindah aplikasi untuk mengecek always-on-top, tutup/buka via tray, lalu restart untuk mengecek pengaturan.

## Batasan MVP

- Spotify Web, perangkat lain, sinkronisasi per kata, kontrol playback, installer, dan startup otomatis belum didukung.
- Polling sesi Windows dilakukan tiap 500 ms; sorotan diperbarui tiap 100 ms menggunakan posisi playback dan timestamp Windows. Ketepatan tetap tergantung data sesi Spotify dan timestamp lirik; gunakan koreksi waktu jika diperlukan.
- Pencarian memakai metadata yang diberikan Spotify. Versi live/remaster atau metadata berbeda dapat menyebabkan lirik tidak ditemukan. Tidak ada pemilihan hasil manual atau scraping Spotify.
- Ketersediaan serta lisensi lirik bergantung pada sumber. Prototipe pribadi ini tidak memberikan hak redistribusi lirik atau persetujuan Spotify untuk distribusi publik.
- Cache tidak memiliki penghapusan otomatis. Aplikasi tidak memerlukan hak administrator, startup entry, atau perubahan registry.

Referensi: [Windows Media Sessions](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager), [timeline playback Windows](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessiontimelineproperties), [LRCLIB API](https://lrclib.net/docs).
