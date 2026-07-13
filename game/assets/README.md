# Assets

Drop final art here. The game runs without any binary assets: **audio is
synthesized at runtime** (see below) and blocks are drawn procedurally, so you
can ship a fully playable, fully-scored build before art is final.

```
assets/
├── fonts/           display + numeric font (a rounded/mono neon face reads best)
└── images/          logo, store icons, screenshots
```

## Audio — no files needed

Every SFX and the music bed are generated as 16-bit PCM at runtime by
`Blockfall.Core.Audio.AudioSynth` (pure C#, unit-tested), then wrapped in an
`AudioStreamWav` by `scripts/Audio/AudioManager.cs`. There is nothing to license,
localize, or lazy-load, and the sound set can't drift out of sync with the game
because it's code. If you later want to override a sound with a recorded sample,
add a file lookup ahead of the synth in `AudioManager.SfxStream`.
