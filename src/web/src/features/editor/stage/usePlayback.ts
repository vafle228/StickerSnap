import { useEffect, type RefObject } from "react";
import type { Segment } from "../../../shared/api/types";
import { useEditor } from "../model/store";
import { frameAt } from "../model/timeline";

type Loop = Pick<Segment, "inFrame" | "outFrame" | "speed" | "loop">;

/** The quick preview (artboard 3a «Цикл · быстрое превью»): the segment plays in a loop at its
 *  speed, ping-pong runs it back. <video> cannot play backwards, so the way back is seeked frame
 *  by frame. Without a playable video the clock still runs, so the read-outs stay alive. */
export function usePlayback(
  videoRef: RefObject<HTMLVideoElement | null>,
  usable: boolean,
  segment: Loop,
  fps: number,
) {
  const playing = useEditor((s) => s.playing);
  const { inFrame, outFrame, speed, loop } = segment;

  useEffect(() => {
    const video = usable ? videoRef.current : null;
    if (!playing) {
      video?.pause();
      return undefined;
    }
    const { setTime, setPlaying } = useEditor.getState();
    const start = inFrame / fps;
    const end = (outFrame + 1) / fps;
    let time = useEditor.getState().time;
    if (time < start || time >= end) time = start;
    let direction = 1;
    let last = performance.now();
    let frame = 0;

    if (video) {
      video.currentTime = time;
      video.playbackRate = speed;
      video.play().catch(() => setPlaying(false));
    }

    const tick = (now: number) => {
      const step = ((now - last) / 1000) * speed;
      last = now;
      if (video && direction === 1) time = video.currentTime;
      else time += step * direction;

      if (direction === 1 && time >= end) {
        if (loop === "pingpong") {
          direction = -1;
          time = end;
          video?.pause();
        } else {
          time = start;
          if (video) video.currentTime = start;
        }
      } else if (direction === -1 && time <= start) {
        direction = 1;
        time = start;
        if (video) {
          video.currentTime = start;
          video.play().catch(() => setPlaying(false));
        }
      } else if (video && direction === -1 && !video.seeking) {
        video.currentTime = time;
      }
      setTime(time);
      frame = requestAnimationFrame(tick);
    };
    frame = requestAnimationFrame(tick);

    return () => {
      cancelAnimationFrame(frame);
      video?.pause();
    };
  }, [videoRef, usable, playing, inFrame, outFrame, speed, loop, fps]);

  // Paused, the video follows the playhead: a step or a scrub on the timeline shows that frame.
  // A store subscription, not a selector: the stage must not re-render on every frame.
  useEffect(() => {
    const video = videoRef.current;
    if (!usable || !video) return undefined;
    // The seek aims at the middle of the frame: at its exact start, float noise in Chromium
    // can show the frame before it.
    const follow = (time: number) => {
      const frame = frameAt(time, fps);
      if (frameAt(video.currentTime, fps) !== frame) video.currentTime = (frame + 0.5) / fps;
    };
    if (!useEditor.getState().playing) follow(useEditor.getState().time);
    return useEditor.subscribe((s, prev) => {
      if (!s.playing && s.time !== prev.time) follow(s.time);
    });
  }, [videoRef, usable, fps]);
}
