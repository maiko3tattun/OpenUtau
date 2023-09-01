using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using NAudio.Dmo;
using OpenUtau.Api;
using OpenUtau.Core.G2p;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Delta English Phonemizer", "EN DELTA", "Lotte V", language: "EN")]
    public class ENDeltaPhonemizer : SyllableBasedPhonemizer {
        /// <summary>
        /// General English phonemizer for Delta list (X-SAMPA) voicebanks.
        /// The difference between this phonemizer and the Teto English phonemizer is that this one was made to support all Delta list-based banks.
        /// However, it should be fully compatible with Kasane Teto's English voicebank regardless.
        /// It also has some support for sounds not found in the "classic" Delta list.
        /// Due to the flexibility of X-SAMPA, it was easy to add those.
        /// They are mostly based on sounds based on Cz's English VCCV list, just written differently. They are mostly found in North-American dialects.
        /// All of these sounds are optional and should be inserted manually/phonetically, if the voicebank supports them.
        ///</summary>

        private readonly string[] vowels = "a,A,@,{,V,O,aU,aI,E,3,eI,I,i,oU,OI,U,u,Q,Ol,Ql,aUn,e@,eN,IN,e,o,Ar,Qr,Er,Ir,Or,Ur,ir,ur,aIr,aUr,A@,Q@,E@,I@,O@,U@,i@,u@,aI@,aU@,@r,@l,@m,@n,@N,1,e@m,e@n,y,I\\,M,U\\,Y,@\\,@`,3`,A`,Q`,E`,I`,O`,U`,i`,u`,aI`,aU`,},2,3\\,6,7,8,9,&,{~,I~,aU~,VI,VU,@U,i:,u:,O:,e@0".Split(',');
        private readonly string[] consonants = "b,tS,d,D,4,f,g,h,dZ,k,l,m,n,N,p,r,s,S,t,T,v,w,W,j,z,Z,t_},・,_".Split(',');
        private readonly string[] affricates = "tS,dZ".Split(',');
        private readonly string[] shortConsonants = "4".Split(",");
        private readonly string[] longConsonants = "tS,f,dZ,k,p,s,S,t,T,t_}".Split(",");
        private readonly string[] normalConsonants = "b,d,D,g,h,l,m,n,N,r,v,w,W,j,z,Z,・".Split(',');
        private readonly Dictionary<string, string> dictionaryReplacements = ("aa=A;ae={;ah=V;ao=O;aw=aU;ax=@;ay=aI;" +
            "b=b;ch=tS;d=d;dh=D;" + "dx=4;eh=E;el=@l;em=@m;en=@n;eng=@N;er=3;ey=eI;f=f;g=g;hh=h;ih=I;iy=i;jh=dZ;k=k;l=l;m=m;n=n;ng=N;ow=oU;oy=OI;" +
            "p=p;q=・;r=r;s=s;sh=S;t=t;th=T;" + "uh=U;uw=u;v=v;w=w;" + "y=j;z=z;zh=Z").Split(';')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);

        // For banks aliased with VOCALOID-style phonemes
        private readonly Dictionary<string, string> vocaSampa = "A=Q;E=e;i=i:;u=u:;O=O:;3=@r".Split(';')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);

        private bool isVocaSampa = false;

        // For banks with slightly fewer vowels
        private readonly Dictionary<string, string> simpleDelta = "E=e;V=@;o=O".Split(';')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);

        private bool isSimpleDelta = false;

        // For banks with only minimal vowels
        private readonly Dictionary<string, string> miniDelta = "I=i;U=u".Split(';')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);

        private bool isMiniDelta = false;

        // For Japanese-English combined banks.
        // NOTE: Rather rudimentary by default; a custom dictionary is recommended.
        private readonly Dictionary<string, string> enPlusJa = "j=y;dZ=j;r=r\\;tS=ch".Split(';')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);

        private bool isEnPlusJa = false;

        // For banks encoded in "true"/more accurate X-SAMPA.
        private readonly Dictionary<string, string> trueXSampa = "r=r\\;3=@`".Split(';')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);

        private bool isTrueXSampa = false;

        protected override string[] GetVowels() => vowels;
        protected override string[] GetConsonants() => consonants;
        protected override string GetDictionaryName() => "cmudict-0_7b.txt";
        protected override Dictionary<string, string> GetDictionaryPhonemesReplacement() => dictionaryReplacements;

        protected override IG2p LoadBaseDictionary() {
            var g2ps = new List<IG2p>();

            // Load dictionary from plugin folder.
            string path = Path.Combine(PluginDir, "xsampa.yaml");
            if (!File.Exists(path)) {
                Directory.CreateDirectory(PluginDir);
                File.WriteAllBytes(path, Data.Resources.xsampa_template);
            }
            g2ps.Add(G2pDictionary.NewBuilder().Load(File.ReadAllText(path)).Build());

            // Load dictionary from singer folder.
            if (singer != null && singer.Found && singer.Loaded) {
                string file = Path.Combine(singer.Location, "xsampa.yaml");
                if (File.Exists(file)) {
                    try {
                        g2ps.Add(G2pDictionary.NewBuilder().Load(File.ReadAllText(file)).Build());
                    } catch (Exception e) {
                        Log.Error(e, $"Failed to load {file}");
                    }
                }
            }
            g2ps.Add(new ArpabetG2p());
            return new G2pFallbacks(g2ps.ToArray());
        }

        protected override string[] GetSymbols(Note note) {
            string[] original = base.GetSymbols(note);
            if (original == null) {
                return null;
            }
            List<string> modified = new List<string>();
            // Splits diphthongs and affricates if not present in the bank
            string[] diphthongs = new[] { "aI", "eI", "OI", "aU", "oU", "VI", "VU", "@U" };
            string[] affricates = new[] { "dZ", "tS" };
            foreach (string s in original) {
                if (diphthongs.Contains(s) && !HasOto($"b{s}", note.tone)) {
                    modified.AddRange(new string[] { s[0].ToString(), s[1].ToString() });
                } else if (affricates.Contains(s) && (!HasOto($"i {s}", note.tone) && !HasOto($"i: {s}", note.tone))) {
                    modified.AddRange(new string[] { s[0].ToString(), s[1].ToString() });
                } else {
                    modified.Add(s);
                }
            }
            return modified.ToArray();
        }

        protected override List<string> ProcessSyllable(Syllable syllable) {
            string prevV = syllable.prevV;
            string[] cc = syllable.cc;
            string v = syllable.v;

            string basePhoneme;
            var phonemes = new List<string>();
            var lastC = cc.Length - 1;
            var firstC = 0;
            var rv = $"- {v}";

            // Switch between phonetic systems, depending on certain aliases in the bank
            if (HasOto("i: b", syllable.tone)) {
                isVocaSampa = true;
            }

            if (!HasOto($"bV", syllable.vowelTone)) {
                isSimpleDelta = true;
            }

            if (!HasOto($"bI", syllable.vowelTone)) {
                isMiniDelta = true;
            }

            if (HasOto("か", syllable.vowelTone)) {
                isEnPlusJa = true;
            }

            if (HasOto("@` r\\", syllable.tone)) {
                isTrueXSampa = true;
            }

            if (syllable.IsStartingV) {
                if (HasOto(rv, syllable.vowelTone) || HasOto(ValidateAlias(rv), syllable.vowelTone)) {
                    basePhoneme = rv;
                } else {
                    basePhoneme = v;
                }
            } else if (syllable.IsVV) {
                var vv = $"{prevV} {v}";
                if (!CanMakeAliasExtension(syllable)) {
                    if (HasOto(vv, syllable.vowelTone) || HasOto(ValidateAlias(vv), syllable.vowelTone)) {
                        basePhoneme = vv;
                    } else {
                        basePhoneme = v;
                    }
                } else {
                    // the previous alias will be extended
                    basePhoneme = null;
                }
            } else if (syllable.IsStartingCVWithOneConsonant) {
                // TODO: move to config -CV or -C CV
                var rcv = $"- {cc[0]}{v}";
                var crv = $"{cc[0]} {v}";
                var cv = $"{cc[0]}{v}";
                if (HasOto(rcv, syllable.vowelTone) || HasOto(ValidateAlias(rcv), syllable.vowelTone)) {
                    basePhoneme = rcv;
                } else if ((!HasOto(rcv, syllable.vowelTone) || !HasOto(ValidateAlias(rcv), syllable.vowelTone)) && (HasOto(crv, syllable.vowelTone) || HasOto(ValidateAlias(crv), syllable.vowelTone))) {
                    basePhoneme = crv;
                    TryAddPhoneme(phonemes, syllable.tone, $"- {cc[0]}", ValidateAlias($"- {cc[0]}"));
                } else {
                    basePhoneme = cv;
                    TryAddPhoneme(phonemes, syllable.tone, $"- {cc[0]}", ValidateAlias($"- {cc[0]}"));
                }
            } else if (syllable.IsStartingCVWithMoreThanOneConsonant) {
                // try RCCV
                var rccv = $"- {string.Join("", cc)}{v}";
                var crv = $"{cc.Last()} {v}";
                var ucv = $"_{cc.Last()}{v}";
                if (HasOto(rccv, syllable.vowelTone) || HasOto(ValidateAlias(rccv), syllable.vowelTone)) {
                    basePhoneme = rccv;
                } else {
                    if (HasOto(ucv, syllable.vowelTone) || HasOto(ValidateAlias(ucv), syllable.vowelTone)) {
                        basePhoneme = ucv;
                    } else if (HasOto(crv, syllable.vowelTone) || HasOto(ValidateAlias(crv), syllable.vowelTone)) {
                        basePhoneme = crv;
                    } else {
                        basePhoneme = $"{cc.Last()}{v}";
                    }
                    // try RCC
                    for (var i = cc.Length; i > 1; i--) {
                        if (TryAddPhoneme(phonemes, syllable.tone, $"- {string.Join("", cc.Take(i))}", ValidateAlias($"- {string.Join("", cc.Take(i))}"))) {
                            firstC = i - 1;
                            break;
                        }
                    }
                    if (phonemes.Count == 0) {
                        TryAddPhoneme(phonemes, syllable.tone, $"- {cc[0]}", ValidateAlias($"- {cc[0]}"));
                    }
                    // try CCV
                    for (var i = firstC; i < cc.Length - 1; i++) {
                        var ccv = string.Join("", cc.Skip(i)) + v;
                        if (HasOto(ccv, syllable.vowelTone) || HasOto(ValidateAlias(ccv), syllable.vowelTone)) {
                            basePhoneme = ccv;
                            lastC = i;
                            break;
                        } else {
                            if (HasOto(ucv, syllable.vowelTone) || HasOto(ValidateAlias(ucv), syllable.vowelTone)) {
                                basePhoneme = ucv;
                                break;
                            } else if (HasOto(crv, syllable.vowelTone) || HasOto(ValidateAlias(crv), syllable.vowelTone)) {
                                basePhoneme = crv;
                                break;
                            } else {
                                basePhoneme = $"{cc.Last()}{v}";
                                break;
                            }
                        }
                    }
                }
            } else { // VCV
                var vcv = $"{prevV} {cc[0]}{v}";
                var vccv = $"{prevV} {string.Join("", cc)}{v}";
                var crv = $"{cc.Last()} {v}";
                if (syllable.IsVCVWithOneConsonant && (HasOto(vcv, syllable.vowelTone) || HasOto(ValidateAlias(vcv), syllable.vowelTone))) {
                    basePhoneme = vcv;
                } else if (syllable.IsVCVWithMoreThanOneConsonant && (HasOto(vccv, syllable.vowelTone) || HasOto(ValidateAlias(vccv), syllable.vowelTone))) {
                    basePhoneme = vccv;
                } else {
                    basePhoneme = cc.Last() + v;
                    if (!HasOto(cc.Last() + v, syllable.vowelTone) && (HasOto(crv, syllable.vowelTone) || HasOto(ValidateAlias(crv), syllable.vowelTone))) {
                        basePhoneme = crv;
                    }
                    // try CCV
                    if (cc.Length - firstC > 1) {
                        for (var i = firstC; i < cc.Length; i++) {
                            var ccv = $"{string.Join("", cc.Skip(i))}{v}";
                            var rccv = $"- {string.Join("", cc.Skip(i))}{v}";
                            if (HasOto(ccv, syllable.vowelTone) || HasOto(ValidateAlias(ccv), syllable.vowelTone)) {
                                lastC = i;
                                basePhoneme = ccv;
                                break;
                            } else if ((HasOto(rccv, syllable.vowelTone) || HasOto(ValidateAlias(rccv), syllable.vowelTone)) && (!HasOto(ccv, syllable.vowelTone) || !HasOto(ValidateAlias(ccv), syllable.vowelTone))) {
                                lastC = i;
                                basePhoneme = rccv;
                                break;
                            }
                        }
                    }
                    // try vcc
                    for (var i = lastC + 1; i >= 0; i--) {
                        var vr = $"{prevV} -";
                        var vcc = $"{prevV} {string.Join("", cc.Take(2))}";
                        var vcc2 = $"{prevV}{string.Join(" ", cc.Take(2))}";
                        var vc = $"{prevV} {cc[0]}";
                        if (i == 0) {
                            if (HasOto(vr, syllable.tone) || HasOto(ValidateAlias(vr), syllable.tone)) {
                                phonemes.Add(vr);
                            }
                        } else if (HasOto(vcc, syllable.tone) || HasOto(ValidateAlias(vcc), syllable.tone)) {
                            phonemes.Add(vcc);
                            firstC = 1;
                            break;
                        } else if (HasOto(vcc2, syllable.tone) || HasOto(ValidateAlias(vcc2), syllable.tone)) {
                            phonemes.Add(vcc2);
                            firstC = 1;
                            break;
                        } else if (HasOto(vc, syllable.tone) || HasOto(ValidateAlias(vc), syllable.tone)) {
                            phonemes.Add(vc);
                            break;
                        } else {
                            continue;
                        }
                    }
                }
            }
            for (var i = firstC; i < lastC; i++) {
                // we could use some CCV, so lastC is used
                // we could use -CC so firstC is used
                var rccv = $"- {string.Join("", cc)}{v}";
                var cc1 = $"{string.Join("", cc.Skip(i))}";
                var ccv = string.Join("", cc.Skip(i)) + v;
                var ucv = $"_{cc.Last()}{v}"; ;
                if (!HasOto(rccv, syllable.vowelTone) || !HasOto(ValidateAlias(rccv), syllable.vowelTone)) {
                    if (!HasOto(cc1, syllable.tone)) {
                        cc1 = ValidateAlias(cc1);
                    }
                    if (!HasOto(cc1, syllable.tone)) {
                        cc1 = $"{cc[i]}{cc[i + 1]}";
                    }
                    if (!HasOto(cc1, syllable.tone)) {
                        cc1 = ValidateAlias(cc1);
                    }
                    if (!HasOto(cc1, syllable.tone)) {
                        cc1 = $"{cc[i]} {cc[i + 1]}";
                    }
                    if (!HasOto(cc1, syllable.tone)) {
                        cc1 = ValidateAlias(cc1);
                    }
                    if (!HasOto(cc1, syllable.tone)) {
                        cc1 = $"{cc[i]} {cc[i + 1]}-";
                    }
                    if (!HasOto(cc1, syllable.tone)) {
                        cc1 = ValidateAlias(cc1);
                    }
                    if (HasOto(ccv, syllable.vowelTone) || HasOto(ValidateAlias(ccv), syllable.vowelTone)) {
                        basePhoneme = ccv;
                    } else if ((HasOto(ucv, syllable.vowelTone) || HasOto(ValidateAlias(ucv), syllable.vowelTone)) && HasOto(cc1, syllable.vowelTone) && !cc1.Contains($"{cc[i]} {cc[i + 1]}")) {
                        basePhoneme = ucv;
                    }
                    if (i + 1 < lastC) {
                        var cc2 = $"{string.Join("", cc.Skip(i))}";
                        if (!HasOto(cc2, syllable.tone)) {
                            cc2 = ValidateAlias(cc2);
                        }
                        if (!HasOto(cc2, syllable.tone)) {
                            cc2 = ValidateAlias(cc2);
                        }
                        if (!HasOto(cc2, syllable.tone)) {
                            cc2 = $"{cc[i + 1]}{cc[i + 2]}";
                        }
                        if (!HasOto(cc2, syllable.tone)) {
                            cc2 = ValidateAlias(cc2);
                        }
                        if (!HasOto(cc2, syllable.tone)) {
                            cc2 = $"{cc[i + 1]} {cc[i + 2]}";
                        }
                        if (!HasOto(cc2, syllable.tone)) {
                            cc2 = ValidateAlias(cc2);
                        }
                        if (!HasOto(cc2, syllable.tone)) {
                            cc2 = $"{cc[i + 1]} {cc[i + 2]}-";
                        }
                        if (!HasOto(cc2, syllable.tone)) {
                            cc2 = ValidateAlias(cc2);
                        }
                        if (HasOto(ccv, syllable.vowelTone) || HasOto(ValidateAlias(ccv), syllable.vowelTone)) {
                            basePhoneme = ccv;
                        } else if ((HasOto(ucv, syllable.vowelTone) || HasOto(ValidateAlias(ucv), syllable.vowelTone)) && (HasOto(cc2, syllable.vowelTone) || HasOto(ValidateAlias(cc2), syllable.vowelTone)) && !cc2.Contains($"{cc[i + 1]} {cc[i + 2]}")) {
                            basePhoneme = ucv;
                        }
                        if (TryAddPhoneme(phonemes, syllable.tone, $"{cc[i]} {cc[i + 1]}{cc[i + 2]}-", ValidateAlias($"{cc[i]} {cc[i + 1]}{cc[i + 2]}-"))) {
                            // like [V C1] [C1 C2C3-] ...
                            i++;
                        } else if (HasOto(cc1, syllable.tone) && HasOto(cc2, syllable.tone) && !cc1.Contains($"{string.Join("", cc.Skip(i))}")) {
                            // like [V C1] [C1 C2] [C2 C3] [C3 ..]
                            phonemes.Add(cc1);
                        } else if (TryAddPhoneme(phonemes, syllable.tone, cc1)) {
                            // like [V C1] [C1 C2] [C2 ..]
                            if (cc1.Contains($"{string.Join("", cc.Skip(i))}")) {
                                i++;
                            }
                        } else if (TryAddPhoneme(phonemes, syllable.tone, $"{cc[i]} {cc[i + 1]}-", ValidateAlias($"{cc[i]} {cc[i + 1]}-"))) {
                            // like [V C1] [C1 C2-] [C3 ..]
                            if (affricates.Contains(cc[i + 1])) {
                                i++;
                            } else {
                                //  continue as usual
                            }
                        } else {
                            // like [V C1] [C1] [C2 ..]
                            TryAddPhoneme(phonemes, syllable.tone, cc[i], ValidateAlias(cc[i]));
                        }
                    } else {
                        // like [V C1] [C1 C2]  [C2 ..] or like [V C1] [C1 -] [C3 ..]
                        TryAddPhoneme(phonemes, syllable.tone, cc1, cc[i], ValidateAlias(cc[i]));
                    }
                }
            }

            phonemes.Add(basePhoneme);
            return phonemes;
        }

        protected override List<string> ProcessEnding(Ending ending) {
            string[] cc = ending.cc;
            string v = ending.prevV;

            var phonemes = new List<string>();
            var vr = $"{v} -";
            if (ending.IsEndingV) {
                TryAddPhoneme(phonemes, ending.tone, vr, ValidateAlias(vr));
            } else if (ending.IsEndingVCWithOneConsonant) {
                var vc = $"{v} {cc[0]}";
                var vcr = $"{v} {cc[0]}-";
                var vcr2 = $"{v}{cc[0]} -";
                if (HasOto(vcr, ending.tone) || HasOto(ValidateAlias(vcr), ending.tone)) {
                    phonemes.Add(vcr);
                } else if ((!HasOto(vcr, ending.tone) || !HasOto(ValidateAlias(vcr), ending.tone)) && (HasOto(vcr2, ending.tone) || HasOto(ValidateAlias(vcr2), ending.tone))) {
                    phonemes.Add(vcr2);
                } else {
                    phonemes.Add(vc);
                    if (affricates.Contains(cc[0])) {
                        TryAddPhoneme(phonemes, ending.tone, $"{cc[0]} -", cc[0]);
                    } else {
                        TryAddPhoneme(phonemes, ending.tone, $"{cc[0]} -", ValidateAlias($"{cc[0]} -"));
                    }
                }
            } else {
                var vcc1 = $"{v} {string.Join("", cc)}-";
                var vcc2 = $"{v}{string.Join(" ", cc)}-";
                var vcc3 = $"{v}{cc[0]} {cc[0 + 1]}-";
                var vcc4 = $"{v}{cc[0]} {cc[0 + 1]}";
                var vc = $"{v} {cc[0]}";
                if (HasOto(vcc1, ending.tone) || HasOto(ValidateAlias(vcc1), ending.tone)) {
                    phonemes.Add(vcc1);
                } else if (HasOto(vcc2, ending.tone) || HasOto(ValidateAlias(vcc2), ending.tone)) {
                    phonemes.Add(vcc2);
                } else if (HasOto(vcc3, ending.tone) || HasOto(ValidateAlias(vcc3), ending.tone)) {
                    phonemes.Add(vcc3);
                } else {
                    if (HasOto(vcc4, ending.tone) || HasOto(ValidateAlias(vcc4), ending.tone)) {
                        phonemes.Add(vcc4);
                    } else {
                        phonemes.Add(vc);
                    }
                    // all CCs except the first one are /C1C2/, the last one is /C1 C2-/
                    // but if there is no /C1C2/, we try /C1 C2-/, vise versa for the last one
                    for (var i = 0; i < cc.Length - 1; i++) {
                        var cc1 = $"{cc[i]} {cc[i + 1]}";
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = $"{cc[i]}{cc[i + 1]}";
                        }
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = $"{cc[i]} {cc[i + 1]}-";
                        }
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        if (i < cc.Length - 2) {
                            var cc2 = $"{cc[i]} {string.Join("", cc.Skip(i))}-";
                            var cc3 = $"{cc[i]} {cc[i + 1]}{cc[i + 2]}-";
                            if (HasOto(cc2, ending.tone) || HasOto(ValidateAlias(cc2), ending.tone)) {
                                phonemes.Add(cc2);
                                i++;
                            } else if (HasOto(cc3, ending.tone) || HasOto(ValidateAlias(cc3), ending.tone)) {
                                // like [C1 C2][C2 ...]
                                phonemes.Add(cc3);
                                i++;
                            } else {
                                if (HasOto(cc1, ending.tone) && (!HasOto(vcc4, ending.tone) || !HasOto(ValidateAlias(vcc4), ending.tone))) {
                                    // like [C1 C2][C2 ...]
                                    phonemes.Add(cc1);
                                } else if (TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]} {cc[i + 2]}-", ValidateAlias($"{cc[i + 1]} {cc[i + 2]}-"))) {
                                    // like [C1 C2-][C2 ...]
                                    i++;
                                } else if (TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]} {cc[i + 2]}", ValidateAlias($"{cc[i + 1]} {cc[i + 2]}"))) {
                                    // like [C1 C2][C3 ...]
                                    if (cc[i + 2] == cc.Last()) {
                                        TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 2]} -", ValidateAlias($"{cc[i + 2]} -"));
                                        i++;
                                    } else {
                                        continue;
                                    }
                                } else if (TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]}{cc[i + 2]}", ValidateAlias($"{cc[i + 1]}{cc[i + 2]}"))) {
                                    // like [C1C2][C3 ...]
                                } else if (!cc.First().Contains(cc[i + 1]) || !cc.First().Contains(cc[i + 2])) {
                                    // like [C1][C2 ...]
                                    if (affricates.Contains(cc[i]) && (!HasOto(vcc4, ending.tone) || !HasOto(ValidateAlias(vcc4), ending.tone))) {
                                        TryAddPhoneme(phonemes, ending.tone, cc[i], ValidateAlias(cc[i]), $"{cc[i]} -", ValidateAlias($"{cc[i]} -"));
                                    }
                                    TryAddPhoneme(phonemes, ending.tone, cc[i + 1], ValidateAlias(cc[i + 1]), $"{cc[i + 1]} -", ValidateAlias($"{cc[i + 1]} -"));
                                    TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 2]} -", ValidateAlias($"{cc[i + 2]} -"), cc[i + 2], ValidateAlias(cc[i + 2]));
                                    i++;
                                } else if (!cc.First().Contains(cc[i])) {
                                    // like [C1][C2 ...]
                                    TryAddPhoneme(phonemes, ending.tone, cc[i], ValidateAlias(cc[i]), $"{cc[i]} -", ValidateAlias($"{cc[i]} -"));
                                    i++;
                                }
                            }
                        } else {
                            if (!HasOto(vcc4, ending.tone)) {
                                if (TryAddPhoneme(phonemes, ending.tone, $"{cc[i]} {cc[i + 1]}-", ValidateAlias($"{cc[i]} {cc[i + 1]}-"))) {
                                    // like [C1 C2-]
                                    i++;
                                } else if (TryAddPhoneme(phonemes, ending.tone, $"{cc[i]}{cc[i + 1]}")) {
                                    // like [C1C2][C2 -]
                                    if (affricates.Contains(cc[i + 1])) {
                                        TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]} -", cc[i + 1]);
                                    } else {
                                        TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]} -", ValidateAlias($"{cc[i + 1]} -"));
                                    }
                                    i++;
                                } else if (TryAddPhoneme(phonemes, ending.tone, cc1)) {
                                    // like [C1 C2][C2 -]
                                    if (affricates.Contains(cc[i + 1])) {
                                        TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]} -", cc[i + 1]);
                                    } else {
                                        TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]} -", ValidateAlias($"{cc[i + 1]} -"));
                                    }
                                    i++;
                                } else {
                                    // like [C1][C2 -]
                                    if (!HasOto(vcc4, ending.tone)) {
                                        TryAddPhoneme(phonemes, ending.tone, cc[i], ValidateAlias(cc[i]), $"{cc[i]} -", ValidateAlias($"{cc[i]} -"));
                                        if (!affricates.Contains(cc[0])) {
                                            phonemes.Remove(cc[0]);
                                        }
                                        TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]} -", ValidateAlias($"{cc[i + 1]} -"), cc[i + 1], ValidateAlias(cc[i + 1]));
                                        i++;
                                    }
                                }
                            } else {
                                if (affricates.Contains(cc[0 + 1])) {
                                    TryAddPhoneme(phonemes, ending.tone, $"{cc[0 + 1]} -", $"{cc[0 + 1]}");
                                } else {
                                    TryAddPhoneme(phonemes, ending.tone, $"{cc[0 + 1]} -", ValidateAlias($"{cc[0 + 1]} -"));
                                }
                                i++;
                            }
                        }
                    }
                }
            }
            return phonemes;
        }

        protected override string ValidateAlias(string alias) {
            // Validate alias depending on method
            if (isVocaSampa) {
                foreach (var syllable in vocaSampa) {
                    alias = alias.Replace(syllable.Key, syllable.Value);
                }
            }

            if (isSimpleDelta) {
                foreach (var syllable in simpleDelta) {
                    alias = alias.Replace(syllable.Key, syllable.Value);
                }
            }

            if (isMiniDelta) {
                foreach (var syllable in miniDelta) {
                    alias = alias.Replace(syllable.Key, syllable.Value);
                }
            }

            if (isEnPlusJa) {
                foreach (var syllable in enPlusJa) {
                    alias = alias.Replace(syllable.Key, syllable.Value);
                }
            }

            if (isTrueXSampa) {
                foreach (var syllable in trueXSampa) {
                    alias = alias.Replace(syllable.Key, syllable.Value);
                }
            }

            // Other validations
            foreach (var CC in new[] { "N k", "N g", "N k-", "N g-" }) {
                alias = alias.Replace("N ", "n ");
            }
            foreach (var consonant1 in new[] { "r ", "r\\ " }) {
                foreach (var consonant2 in consonants) {
                    alias = alias.Replace(consonant1 + consonant2, "3 " + consonant2);
                }
            }
            return alias;
        }

        protected override double GetTransitionBasicLengthMs(string alias = "") {
            foreach (var c in longConsonants) {
                if (alias.Contains(c)) {
                    if (!alias.StartsWith(c)) {
                        return base.GetTransitionBasicLengthMs() * 2.0;
                    }
                }
            }
            foreach (var c in normalConsonants) {
                if (!alias.Contains("_D")) {
                    if (alias.Contains(c)) {
                        if (!alias.StartsWith(c)) {
                            return base.GetTransitionBasicLengthMs();
                        }
                    }
                }
            }

            foreach (var c in shortConsonants) {
                if (alias.Contains(c)) {
                    if (!alias.Contains(" _")) {
                        return base.GetTransitionBasicLengthMs() * 0.50;
                    }
                }
            }
            return base.GetTransitionBasicLengthMs();
        }
    }
}
