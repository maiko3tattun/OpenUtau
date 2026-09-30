using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Midi;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.SourceGenerators;
using Serilog;
using static ReactiveUI.Primitives.SubscribeExtensions;

namespace OpenUtau.App.ViewModels {
    public partial class MidiInViewModel : ViewModelBase, ICmdSubscriber {
        private static MidiIn? midiIn;
        private NotesViewModel notesViewModel;
        private UVoicePart? part => notesViewModel.Part;

        [Reactive] public partial bool RecReady { get; set; } = true;
        [Reactive] public partial int MidiDeviceNum { get; set; } = -1;

        private int activeTone = 0;
        private UNote? recordingNote;
        private CancellationTokenSource? _noteExpandCts;
        private int ExpandIntervalMs = 300; // 更新間隔＝入力感度 (ミリ秒) Prefs
        private bool viewUpdating = true;

        public MidiInViewModel(NotesViewModel notesViewModel) {
            this.notesViewModel = notesViewModel;
            
            this.WhenAnyValue(x => x.RecReady)
                .Subscribe(value => {
                    if (midiIn != null) {
                        StopRecording();
                    }
                });
            this.WhenAnyValue(x => x.MidiDeviceNum)
                .Subscribe(value => {
                    if (viewUpdating) return;
                    try {
                        if (value == -1) {
                            StopMidiInput();
                            return;
                        }
                        SelectMidiDevice(value);
                    } catch (Exception e) {
                        Log.Error(e, "Failed to select MIDI device");
                        DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
                        StopMidiInput();
                    }
                });

            DocManager.Inst.AddSubscriber(this);
            viewUpdating = false;
        }

        public bool TryInitDevice() {
            try {
                int deviceCount = MidiIn.NumberOfDevices;
                if (deviceCount == 0) {
                    DocManager.Inst.ExecuteCmd(new ToastNotification("Pianoroll", "No MIDI input device was found.", "No MIDI input device was found.")); // Todo
                    StopMidiInput();
                    return false;
                }

                Dictionary<int, string> deviceDict = new Dictionary<int, string>();
                for (int i = 0; i < deviceCount; i++) {
                    deviceDict.Add(i, MidiIn.DeviceInfo(i).ProductName);
                }
                int midiInDeviceNumber = deviceCount - 1;
                // preferenceを参照
                SelectMidiDevice(midiInDeviceNumber);
                return true;
            } catch (Exception e) {
                Log.Error(e, "Failed to open MIDI device");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
                StopMidiInput();
                return false;
            }
        }

        public void SelectMidiDevice(int deviceNumber) {
            StopMidiInput();
            if (deviceNumber < 0 || deviceNumber >= MidiIn.NumberOfDevices) {
                viewUpdating = true;
                MidiDeviceNum = -1;
                viewUpdating = false;
                throw new ArgumentOutOfRangeException(nameof(deviceNumber), "Invalid MIDI device number.");
            }

            midiIn = new MidiIn(deviceNumber);
            midiIn.MessageReceived += MidiIn_MessageReceived;
            midiIn.ErrorReceived += MidiIn_ErrorReceived;
            midiIn.Start();

            string deviceName = MidiIn.DeviceInfo(deviceNumber).ProductName;
            Log.Debug($"MIDI input device selected: {deviceName}");
            // preference保存
            viewUpdating = true;
            MidiDeviceNum = deviceNumber;
            viewUpdating = false;
        }

        public void StopMidiInput() {
            StopRecording();
            if (midiIn != null) {
                midiIn.Stop();
                midiIn.Dispose();
                midiIn = null;
            }
            //RecReady = false;
        }

        public void StopRecording() {
            NoteOff(activeTone);
        }

        private void MidiIn_MessageReceived(object? sender, MidiInMessageEventArgs arg) {
            try {
                MidiCommandCode commandCode = arg.MidiEvent.CommandCode;
                switch (commandCode) {
                    case MidiCommandCode.NoteOn:
                        NoteOnEvent noteOn = (NoteOnEvent)arg.MidiEvent;
                        if (noteOn.Velocity > 0) {
                            Log.Debug($"[MIDI NOTE ON] Channel: {noteOn.Channel}, Pitch: {noteOn.NoteNumber} ({noteOn.NoteName}), Velocity: {noteOn.Velocity}");
                            NoteOn(noteOn.NoteNumber);
                        } else {
                            Log.Debug($"[MIDI NOTE OFF (Velocity=0)] Channel: {noteOn.Channel}, Pitch: {noteOn.NoteNumber} ({noteOn.NoteName}), Velocity: {noteOn.Velocity}");
                            NoteOff(noteOn.NoteNumber);
                        }
                        break;
                    case MidiCommandCode.NoteOff:
                        NoteEvent noteOff = (NoteEvent)arg.MidiEvent;
                        Log.Debug($"[MIDI NOTE OFF] Channel: {noteOff.Channel}, Pitch: {noteOff.NoteNumber} ({noteOff.NoteName})");
                        NoteOff(noteOff.NoteNumber);
                        break;
                    case MidiCommandCode.ControlChange:
                        ControlChangeEvent controlChange = (ControlChangeEvent)arg.MidiEvent;
                        Log.Debug($"[MIDI CONTROL CHANGE] Channel: {controlChange.Channel}, Controller: {controlChange.Controller}, Value: {controlChange.ControllerValue}");
                        break;
                    default:
                        Log.Debug($"[MIDI OTHER MESSAGES] Command: {commandCode}");
                        break;
                }
            } catch (Exception e) {
                Log.Error(e, "Error processing MIDI message");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        private void NoteOn(int tone) {
            if (part == null) return;

            // Monitor
            if (activeTone != tone) {
                NoteOff(activeTone);
            }
            PlaybackManager.Inst.PlayTone(MusicMath.ToneToFreq(tone));
            activeTone = tone;

            if (!RecReady) return;

            var playhead = DocManager.Inst.playPosTick;
            if (playhead < part.position || part.End <= playhead) {
                DocManager.Inst.ExecuteCmd(new ToastNotification("Pianoroll", "The playhead is not within the range of the part.", "The playhead is not within the range of the part.")); // Todo
                return;
            }
            playhead = playhead - part.position;
            var project = DocManager.Inst.Project;
            var SnapDiv = notesViewModel.SnapDiv;
            int snapUnit = project.resolution * 4 / SnapDiv;
            int snappedTick = (int)Math.Floor((double)playhead / snapUnit) * snapUnit;

            UNote note = project.CreateNote(tone, snappedTick, snapUnit);
            recordingNote = note;

            // Extend note while holding the key
            _noteExpandCts?.Cancel();
            _noteExpandCts = new CancellationTokenSource();
            var token = _noteExpandCts.Token;
            if (PlaybackManager.Inst.PlayingMaster) {
                // MIDI Recording
                Task.Run(async () => {
                    try {
                        while (!token.IsCancellationRequested) {
                            await Task.Delay(50, token);
                            if (!PlaybackManager.Inst.PlayingMaster) {
                                StopRecording();
                                return;
                            }
                            playhead = DocManager.Inst.playPosTick - part.position;
                            snappedTick = (int)Math.Floor((double)playhead / snapUnit) * snapUnit;
                            if (recordingNote != null && recordingNote.End < snappedTick) {
                                DocManager.Inst.PostOnUIThread(() => {
                                    DocManager.Inst.ExecuteCmd(new ResizeNoteCommand(part, recordingNote, snappedTick - recordingNote.End));
                                });
                            }
                        }
                    } catch (TaskCanceledException) {
                        // Do nothing when the key is released
                    }
                }, token);
            } else {
                // Step Input
                Task.Run(async () => {
                    try {
                        while (!token.IsCancellationRequested) {
                            await Task.Delay(ExpandIntervalMs, token);
                            if (recordingNote != null) {
                                DocManager.Inst.PostOnUIThread(() => {
                                    DocManager.Inst.ExecuteCmd(new ResizeNoteCommand(part, recordingNote, snapUnit));
                                    DocManager.Inst.ExecuteCmd(new SetPlayPosTickNotification(note.End + part.position));
                                });
                            }
                        }
                    } catch (TaskCanceledException) {
                        // Do nothing when the key is released
                    }
                }, token);
            }

            // Create new note
            DocManager.Inst.PostOnUIThread(() => {
                DocManager.Inst.StartUndoGroup("command.note.add");
                DocManager.Inst.ExecuteCmd(new AddNoteCommand(part, note));
                DocManager.Inst.ExecuteCmd(new SetPlayPosTickNotification(note.End + part.position));
            });
        }

        private void NoteOff(int tone) {
            // Monitor
            PlaybackManager.Inst.EndTone(MusicMath.ToneToFreq(tone));
            activeTone = 0;

            if (_noteExpandCts != null && recordingNote != null && recordingNote.tone == tone) {
                _noteExpandCts.Cancel();
                _noteExpandCts = null;
                DocManager.Inst.PostOnUIThread(DocManager.Inst.EndUndoGroup);
                recordingNote = null;
            }
        }

        private void MidiIn_ErrorReceived(object? sender, MidiInMessageEventArgs e) {
            DocManager.Inst.ExecuteCmd(new ToastNotification("Pianoroll", "MIDI input error", $"MIDI input error: {e.RawMessage}"));
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is LoadPartNotification
                || cmd is LoadProjectNotification
                || cmd is FocusNoteNotification
                || cmd is ValidateProjectNotification
                || cmd is SingersRefreshedNotification
                || cmd is RemovePartCommand
                || cmd is MovePartCommand) {
                StopRecording();
            }
        }
    }
}
