# Changelog

## v0.1.0 — 2026-10-26

Rilis awal Mini Lyrics for Spotify untuk Windows x64.

- Deteksi lagu Spotify desktop melalui Windows Media Sessions, tanpa login atau token Spotify.
- Lirik LRCLIB dengan cache lokal dan fallback teks biasa.
- Tampilan transparan dengan teks kuning dan shadow, auto-scroll halus, serta fade di bagian atas/bawah.
- Sorotan baris aktif mengikuti playback; penanganan pause, seek, dan pergantian lagu.
- Mode tembus klik yang dapat dinonaktifkan melalui tray.
- Panel biasa sebagai pilihan, pengaturan ukuran font/opasitas/koreksi waktu/always-on-top, serta posisi yang tersimpan.
- Perbaikan drag pada area kosong header dan judul lagu.
- Pengujian parser, jam playback, request cancellation, cache, UI, transparansi, auto-scroll, dan target mouse Windows.

### Batasan

- Hanya Spotify desktop pada komputer yang sama; browser dan perangkat lain belum didukung.
- Ketepatan dan ketersediaan lirik bergantung pada metadata lagu, timeline Windows, dan LRCLIB.
- EXE belum ditandatangani secara digital; belum tersedia installer, startup otomatis, atau update otomatis.
- Bukan aplikasi resmi Spotify. Rilis ini tidak memberikan lisensi untuk redistribusi konten lirik.
