# Prototype Plan — Camera-Tracked Sports Collection (working title: "MotionSports")

## Status
- Phase 0: setup done (Unity 6, Android, MediaPipe plugin 0.16.3, FPS counter). Device measurements still to do.
- Phase 1: items 1-5 written (pose source, smoothing, recorder/playback, robot avatar, calibration + pause); waiting for testing in Unity.

## Context
A mobile game in the spirit of motion-sports party games: a set of sports played by moving your body in front of the phone camera, with an on-screen 3D avatar that mirrors you. The project started from an empty Unity project.

**Decisions so far**
| Topic | Decision |
|---|---|
| Platform | Android first (iOS later from the same project) |
| Play setup | Phone on a stand about 1–1.5 m away; **upper-body tracking** (head, shoulders, arms, hands) |
| Sports in prototype | **Boxing** and **Table tennis** (tennis can reuse the same swing system later) |
| Developer | Experienced solo developer |
| Avatar | Stylized, **non-human-looking** character driven by IK; built so customization can be added later |
| Opponents | Single player vs AI now; **online 1v1 after the prototype**, so the design should allow for it |
| Prototype goal | Show that it's fun and that it works: responsive tracking matters more than polished art |
| Extras in scope | Calibration and safety flow; Replay and share clips |

---

## Tech stack & tools

### Game engine: **Unity 6 LTS** (Universal Render Pipeline)
Why Unity over Godot or Unreal for this game:
- It has the most mature pose-tracking integrations on mobile: the MediaPipe Unity Plugin, plus Unity Sentis/Inference Engine for running ONNX models on the device.
- The Android build pipeline is reliable, and so is the later port to iOS.
- The **Animation Rigging** package gives ready-made IK constraints (Two-Bone IK for arms, Multi-Aim for the head), which is exactly what's needed to map a tracked pose onto an avatar.
- There's a clear path to online play later: Netcode for GameObjects + Unity Relay/Lobby, or Photon Fusion.

### Programming languages
| Language | Used for |
|---|---|
| **C#** | About 95% of the code: gameplay, tracking pipeline, IK, UI, AI |
| **Kotlin/Java** (small amount) | Android native bridge: screen and video capture for replay clips (MediaProjection/MediaCodec) and the Android share sheet |
| **HLSL / Shader Graph** | Stylized toon shading for the avatar and effects |
| **Python** (optional) | Offline tools: analysing recorded pose data and tuning the gesture thresholds |

### Pose tracking
- **Primary: MediaPipe Pose Landmarker** (via the open-source `homuler/MediaPipeUnityPlugin`, GPU delegate). It gives 33 body landmarks plus "world" 3D coordinates in meters. Use the *lite* model on low-end phones and *full* on mid- and high-end phones.
- **Optional add-on: MediaPipe Hand Landmarker.** Use it later for fist versus open hand, or for paddle angle.
- **Fallback if performance falls short:** MoveNet/BlazePose as ONNX run through Unity Sentis.
- All processing **stays on the device**: no camera frames are ever uploaded. This matters for privacy and for Play Store review.

### Programs to install
| Program | Purpose |
|---|---|
| Unity Hub + **Unity 6 LTS** with Android Build Support (SDK, NDK, OpenJDK) | Engine |
| **JetBrains Rider** or VS Code + C# Dev Kit | Code editor |
| **Android Studio** | Native Kotlin plugin, `adb`, Logcat, GPU and thermal profiling |
| **Blender** | Model, rig and weight-paint the stylized avatar and the modular parts |
| **Mixamo** (web) | Quick humanoid rig and placeholder animations (idle, victory, knockdown) |
| **Git + Git LFS** + GitHub | Version control for code and binary assets |
| Unity packages | Animation Rigging, Input System, Cinemachine, URP, TextMeshPro, Unity Profiler |
| Audio: **Audacity** + free SFX libraries (e.g. Sonniss GDC packs, freesound) | Hit, crowd and UI sounds |
| Hardware | 1 mid-range Android phone, 1 budget Android phone, a phone tripod, good room lighting |

---

## Important design ideas & suggestions

1. **Latency decides whether the game works.** Target an end-to-end delay (from your movement to the avatar's movement) under ~120 ms, with tracking at 24–30 fps or more. Measure this in week 1, before building gameplay.
2. **Add "intent" assistance instead of relying on exact physics.** Phone-camera depth is noisy, so judge actions from movement *patterns* rather than exact 3D collisions. For example, a punch counts when the wrist moves fast and the arm extends; a table tennis shot counts when the swing happens inside a timing window, and the swing's direction picks where the ball goes. This is the main thing that makes camera sports feel fair.
3. **Keep input as a separate layer.** Pipeline: `Camera → PoseProvider → Filter → PoseFrame → GestureRecognizers → GameplayIntents`. Gameplay never reads the camera directly. This gives three benefits:
   - **Record and replay pose files in the Editor.** You can test and tune without standing up every time, which saves a huge amount of development time.
   - **Online later is easy.** You send compact pose frames and intents over the network (about 20 Hz) to drive the remote player's avatar.
   - **Debug input.** You can test with the keyboard or mouse.
4. **Smooth the tracking with a One-Euro filter** on each landmark. It removes jitter while keeping fast punches responsive.
5. **Design for a mirror image.** Show the avatar from behind or as a mirror so left and right feel natural, and support left-handed play in table tennis.
6. **Calibration flow.** The game shows a silhouette and asks the player to stand so their shoulders and hands fit inside it, then raise both arms (T-pose) to measure arm length and shoulder width. It then checks lighting and backlight and gives hints like "move back", "too dark" or "move into the center".
7. **Safety.** Show a clear-space warning, pause automatically when tracking is lost, and keep rounds short (60–90 s) to manage fatigue and phone heating.
8. **Avatar style.** A non-human, toy-like or robot-mascot character (e.g. a chunky robot or a creature with floating hands) that uses a **humanoid skeleton**, so Mixamo and Unity Humanoid tools still work. Build it from **swappable modular parts** (head, body, hands, colors via material properties) so customization later is a matter of data, not a rework. Floating, detached hands also hide awkward elbow poses when tracking is imperfect.
9. **Replay clips.** Keep a rolling buffer of game frames at reduced resolution (or record the native Android screen) and export the last ~10 s as an MP4 after a knockout or a great rally, then open the Android share sheet. Show only the avatar view, never the raw camera view, unless the player opts in.
10. **Watch the phone's heat and battery.** Profile on the budget phone. Lower the tracking model size or frame rate when the phone gets hot.

---

## Project structure (Unity)
```
Assets/
  _Project/
    Core/          GameManager, SceneLoader, Settings, ServiceLocator
    Tracking/      IPoseProvider, MediaPipePoseProvider, RecordedPoseProvider,
                   KeyboardPoseProvider, OneEuroFilter, PoseFrame, Calibration
    Gestures/      PunchRecognizer, BlockRecognizer, SwingRecognizer, DodgeRecognizer
    Avatar/        AvatarRig (IK targets), AvatarCustomizationData (ScriptableObject)
    Sports/
      Boxing/      BoxingMatch, BoxingAI, HitResolver, Scene
      TableTennis/ BallPhysics, RallyController, TableTennisAI, Scene
    UI/            MainMenu, SportSelect, CalibrationScreen, HUD, Results
    Replay/        ReplayRecorder, ShareBridge (C#↔Kotlin)
  Plugins/Android/ replay + share Kotlin plugin (.aar)
  Art/ Audio/ Models/
```

---

## Execution phases (estimates for an experienced solo developer)

### Phase 0 — Technical test, go/no-go decision (1–1.5 weeks)
- Set up a Unity 6 URP Android project, Git LFS, and the MediaPipe plugin.
- Show the camera image with the tracked skeleton drawn on top, running on the device.
- Measure fps, end-to-end latency (filming the phone with a second phone in slow motion) and heat after 10 minutes, on both test phones.
- **Go/no-go:** at least 24 fps with tracking and less than ~150 ms latency on the mid-range phone. If not, test the lite model, lower resolution or the Sentis fallback.

#### Phase 0 — step-by-step (Git LFS installed, Unity project already created)
1. **Git:** add the Unity `.gitignore` (github/gitignore → `Unity.gitignore`). Run `git lfs track "*.fbx" "*.png" "*.psd" "*.wav" "*.bytes" "*.tflite"`, then make the first commit and push.
2. **Switch to Android:** File → Build Profiles → Android → Switch Platform.
3. **Player Settings:**
   - Scripting Backend = IL2CPP; Target Architectures = ARM64 only; Minimum API Level = 24 or higher.
   - Turn off Auto Graphics API and use **OpenGLES3 only**, because the MediaPipe GPU mode needs it.
   - Set a camera usage description.
4. **Phone:** enable Developer Options and USB debugging, connect it by USB, and check that it shows up under Build Profiles → Run Device.
5. **MediaPipe plugin:** download the latest `com.github.homuler.mediapipe-*.tgz` from the plugin's GitHub Releases, then use Package Manager → "+" → Install package from tarball. Import its **Pose Landmark Detection** sample (Tasks API). *Don't use "Add package from git URL": the repo doesn't include the native libraries or models, so a git install won't work unless you build the plugin yourself.*
6. **Run in the Editor** with your webcam and confirm the skeleton overlay works.
7. **Build And Run on the phone** with the *full* model, then with *lite*.
8. **Add 2 counters** on screen: render FPS and **tracking FPS** (pose results received per second).
9. **Latency test:** film your hand next to the phone screen with a second phone in 240 fps slow motion. Latency (ms) = frames between your hand moving and the skeleton moving × 4.17.
10. **Heat test:** run for 10 minutes and check `adb shell dumpsys thermalservice` and whether FPS drops.
11. Repeat steps 7–10 on the budget phone. **Go/no-go:** tracking at 24 fps or higher and latency under ~150 ms on the mid-range phone.

### Phase 1 — Core framework (1.5–2 weeks)
- `IPoseProvider` abstraction plus the MediaPipe, recorded-file and keyboard providers.
- One-Euro filtering and a coordinate space normalized around the shoulders (hips are usually out of frame in upper-body mode).
- Calibration screen (silhouette fit, T-pose measurement, lighting check) and pause on tracking loss.
- A placeholder stylized avatar with Animation Rigging IK: hand targets come from the wrists, elbow hints from the elbows, head aim from the nose and ears.
- A pose recorder that saves sessions to JSON or binary for playback in the Editor.

### Phase 2 — Boxing (2 weeks)
- Gesture recognition for jab and cross, hooks (sideways wrist path), guard/block (both hands near the face) and dodge (head or shoulder shifts left or right).
- An AI opponent with telegraphed attacks (wind-up animation, then a window to block or dodge) and difficulty tiers.
- Hit resolution, stamina and health, knockdown, 3 rounds × 60 s.
- Game feel ("juice"): hit-stop, screen shake, impact effects, crowd sounds, controller vibration.

### Phase 3 — Table tennis (2–2.5 weeks)
- The player's hand holds a paddle; the swing recognizer reads swing speed, direction and timing relative to when the ball arrives.
- Ball physics on a scripted arc with spin approximated as a curve; the return is chosen from swing direction and power, so a small timing window is assisted.
- An AI opponent with placement and speed tiers, scoring to 11, serve rotation.
- A camera angle behind the avatar, plus a landing marker that shows where the ball will bounce for readability.

### Phase 4 — Game shell & replay (1–1.5 weeks)
- Main menu, sport select, results screen with stats (punches thrown, accuracy, longest rally).
- Replay: rolling frame buffer → MP4 export via a Kotlin MediaCodec plugin → Android share sheet.
- Settings: handedness, difficulty, tracking quality (lite/full), mirror toggle.

### Phase 5 — Playtesting & tuning (1+ week, ongoing)
- Test with 5–10 people of different heights, rooms and lighting.
- Log the false-positive and missed-gesture rates using recorded pose sessions, and tune the thresholds offline.

**Total: about 9–11 weeks to a playable prototype.**

### After the prototype (roadmap, design for it now)
- **Online 1v1:** use Unity Netcode + Relay/Lobby (or Photon Fusion). Send pose frames and intents, never video. The server or host decides hits. Add lag compensation for table tennis.
- **Avatar customization:** a part catalog stored as ScriptableObjects, a color picker, unlockable items.
- More sports (archery, volleyball blocking, goalkeeper), an iOS port, a fitness/stats mode.

---

## Verification / success criteria
- **Performance:** at least 24 fps with tracking on the mid-range phone and at least 20 fps on the budget phone; latency under 150 ms; no heat throttling within a 10-minute session.
- **Gesture accuracy:** using recorded pose test sessions in Editor play mode, punch detection at least 90% correct with fewer than 5% false triggers, and swing detection at least 85% correct.
- **Calibration:** a new player goes from launch to first punch in under 60 s without help.
- **Replay:** the exported clip plays in WhatsApp and Instagram, and the share sheet opens.
- **Fun check (the real goal):** after playtests, most testers want "one more round", and tracking complaints are not the main feedback.
