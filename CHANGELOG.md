# Changelog

## v0.1.4 — 2026-10-27

- Tampilkan highlight preview baris pertama sejak dimuat, termasuk sebelum window terlihat dan saat timestamp dimulai dari nol; cue `[Intro]`/`(Instrumental)` dilewati.
- Perjelas highlight dengan teks tebal dan redupkan baris lain; lirik tanpa timestamp mendapat preview statis berlabel TEKS · PREVIEW.
- Perbaiki romanisasi Korea yang batal untuk seluruh lagu saat menemui 받침 gabungan seperti ㄶ/ㅀ/ㄺ; dukung pemisahan liaison dan aspirasi sebelum memanggil library.
- Cari lirik bertimestamp alternatif ketika hasil awal/cache lama hanya teks biasa, dengan kecocokan judul, artis, durasi, dan prioritas album.
- Tambahkan mode Smooth kontinu berbasis timestamp di panel/tray; posisi mengikuti playback, pause, seek, dan koreksi waktu.
- Saat intro, pusatkan dan terangkan baris pertama yang akan dinyanyikan dengan melewati cue kosong/not musik; timestamp playback tetap dipertahankan.
- Tambahkan spacer awal/akhir agar baris pertama langsung di tengah dan tidak masuk fade, termasuk lirik pendek, tanpa timestamp, dan Latin + Asli setelah konversi.
- Romanisasi lokal Korea/Hangul dan Jepang/kana-kanji, dengan pilihan Asli, Latin, atau Latin + Asli pada panel dan tray.
- Deteksi bahasa otomatis dengan pilihan manual Korea/Jepang, pemrosesan background, cache konversi, dan pembatalan hasil lama saat lagu atau pilihan berubah.
- Bundel kamus Jepang dan notices lisensi dependency; dukungan Mandarin ditunda.
- Retry otomatis saat lirik gagal dimuat karena koneksi, timeout, HTTP 408/429, atau error server; jeda bertahap hingga 60 detik dan menghormati Retry-After.
- Batalkan retry saat lagu berganti, koneksi sesi terputus, retry manual, atau aplikasi keluar; tampilkan status menunggu retry pada panel dan mode teks.
- Tampilkan mode teks dan tembus klik pada panel Pengaturan.
- Sediakan seluruh pengaturan melalui menu tray, termasuk always-on-top dan input angka font, opasitas, serta koreksi waktu.
- Sinkronkan perubahan panel dan tray secara dua arah; panel tetap menerima klik selama pengaturan terbuka.

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
