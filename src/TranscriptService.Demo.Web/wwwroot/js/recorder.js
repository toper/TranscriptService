// Microphone capture for the demo page. Audio is split into utterances on silence and each one is
// handed to .NET as a 16 kHz mono PCM16 WAV, the format the Transcript API transcribes fastest.

const TARGET_RATE = 16000;
const SILENCE_RMS = 0.012;      // frame energy below this counts as silence
const SILENCE_MS = 700;         // this much silence after speech ends an utterance
const MIN_SPEECH_MS = 300;      // shorter bursts (clicks, coughs) are dropped
const MAX_UTTERANCE_MS = 15000; // long monologues are cut so text keeps flowing

// Runs on the audio thread and forwards each 128-sample frame to the page.
const workletSource = `
class Capture extends AudioWorkletProcessor {
  process(inputs) {
    const channel = inputs[0][0];
    if (channel) this.port.postMessage(channel.slice(0));
    return true;
  }
}
registerProcessor('capture', Capture);`;

let session = null;

export async function start(dotnet) {
  if (session) return;
  const stream = await navigator.mediaDevices.getUserMedia({
    audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true },
  });
  const context = new AudioContext();
  const moduleUrl = URL.createObjectURL(new Blob([workletSource], { type: 'text/javascript' }));
  await context.audioWorklet.addModule(moduleUrl);
  URL.revokeObjectURL(moduleUrl);

  const node = new AudioWorkletNode(context, 'capture');
  session = { dotnet, stream, context, node, rate: context.sampleRate, frames: [], speechMs: 0, silenceMs: 0 };
  const current = session;
  node.port.onmessage = (event) => onFrame(current, event.data);
  context.createMediaStreamSource(stream).connect(node);
}

export async function stop() {
  if (!session) return;
  const current = session;
  session = null;
  flush(current);
  current.node.disconnect();
  current.stream.getTracks().forEach((track) => track.stop());
  await current.context.close();
}

function onFrame(s, frame) {
  let energy = 0;
  for (const sample of frame) energy += sample * sample;
  const speaking = Math.sqrt(energy / frame.length) > SILENCE_RMS;
  const frameMs = (frame.length / s.rate) * 1000;

  if (speaking || s.speechMs > 0) s.frames.push(frame);
  if (speaking) {
    s.speechMs += frameMs;
    s.silenceMs = 0;
  } else if (s.speechMs > 0) {
    s.silenceMs += frameMs;
  }

  const lengthMs = s.frames.length * frameMs;
  if ((s.speechMs > 0 && s.silenceMs >= SILENCE_MS) || lengthMs >= MAX_UTTERANCE_MS) flush(s);
}

function flush(s) {
  const { frames, speechMs } = s;
  s.frames = [];
  s.speechMs = 0;
  s.silenceMs = 0;
  if (speechMs < MIN_SPEECH_MS) return;

  const samples = downsample(concat(frames), s.rate, TARGET_RATE);
  s.dotnet.invokeMethodAsync('OnUtterance', encodeWav(samples, TARGET_RATE));
}

function concat(frames) {
  const result = new Float32Array(frames.reduce((length, frame) => length + frame.length, 0));
  let offset = 0;
  for (const frame of frames) {
    result.set(frame, offset);
    offset += frame.length;
  }
  return result;
}

// Averages the input over each output sample's span: a box low-pass filter that keeps the speech
// band intact while preventing aliasing from 48 kHz microphones.
function downsample(samples, fromRate, toRate) {
  if (fromRate === toRate) return samples;
  const ratio = fromRate / toRate;
  const result = new Float32Array(Math.floor(samples.length / ratio));
  for (let i = 0; i < result.length; i++) {
    const start = Math.floor(i * ratio);
    const end = Math.min(samples.length, Math.floor((i + 1) * ratio));
    let sum = 0;
    for (let j = start; j < end; j++) sum += samples[j];
    result[i] = sum / Math.max(1, end - start);
  }
  return result;
}

function encodeWav(samples, rate) {
  const buffer = new ArrayBuffer(44 + samples.length * 2);
  const view = new DataView(buffer);
  const text = (offset, value) => [...value].forEach((char, i) => view.setUint8(offset + i, char.charCodeAt(0)));
  text(0, 'RIFF');
  view.setUint32(4, 36 + samples.length * 2, true);
  text(8, 'WAVE');
  text(12, 'fmt ');
  view.setUint32(16, 16, true);       // fmt chunk size
  view.setUint16(20, 1, true);        // PCM
  view.setUint16(22, 1, true);        // mono
  view.setUint32(24, rate, true);
  view.setUint32(28, rate * 2, true); // byte rate
  view.setUint16(32, 2, true);        // block align
  view.setUint16(34, 16, true);       // bits per sample
  text(36, 'data');
  view.setUint32(40, samples.length * 2, true);
  for (let i = 0; i < samples.length; i++) {
    const clamped = Math.max(-1, Math.min(1, samples[i]));
    view.setInt16(44 + i * 2, clamped < 0 ? clamped * 0x8000 : clamped * 0x7fff, true);
  }
  return new Uint8Array(buffer);
}
