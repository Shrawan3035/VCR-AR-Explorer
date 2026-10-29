import { useCallback, useEffect, useRef, useState } from 'react';

export type CameraState = 'off' | 'starting' | 'on' | 'error';

/**
 * Live camera feed for the "camera overlay" mode: the 3D rig is drawn over the video.
 * The camera is only requested when start() is called (a user action) and every track is
 * stopped on stop() / unmount. Prefers the rear camera on phones, any webcam on laptops.
 */
export function useCameraFeed() {
  const videoRef = useRef<HTMLVideoElement>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const [state, setState] = useState<CameraState>('off');
  const [error, setError] = useState('');

  const stop = useCallback(() => {
    streamRef.current?.getTracks().forEach((t) => t.stop());
    streamRef.current = null;
    if (videoRef.current) videoRef.current.srcObject = null;
    setState('off');
  }, []);

  const start = useCallback(async () => {
    setError('');
    if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia) {
      setState('error');
      setError('The camera needs a secure (https) page and a browser that supports camera access.');
      return;
    }
    setState('starting');
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: { ideal: 'environment' }, width: { ideal: 1280 }, height: { ideal: 720 } },
        audio: false,
      });
      streamRef.current = stream;
      const v = videoRef.current;
      if (v) {
        v.srcObject = stream;
        await v.play().catch(() => undefined);
      }
      setState('on');
    } catch (e) {
      const name = e instanceof DOMException ? e.name : '';
      setState('error');
      setError(
        name === 'NotAllowedError' || name === 'SecurityError'
          ? 'Camera permission was denied. Allow camera access for this site in your browser settings to use the overlay — the 3D view keeps working without it.'
          : name === 'NotFoundError' || name === 'OverconstrainedError'
            ? 'No camera was found on this device.'
            : name === 'NotReadableError'
              ? 'The camera is being used by another app. Close it and try again.'
              : 'The camera could not be started.',
      );
    }
  }, []);

  useEffect(() => stop, [stop]);

  return { videoRef, state, error, start, stop, clearError: () => setError('') };
}
