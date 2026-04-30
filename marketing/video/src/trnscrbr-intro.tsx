import React from 'react';
import {
  AbsoluteFill,
  Img,
  interpolate,
  spring,
  staticFile,
  useCurrentFrame,
  useVideoConfig,
} from 'remotion';

type IntroProps = {
  domain: string;
};

const teal = '#47f0df';
const violet = '#8b72ff';
const lime = '#d9ff76';
const ink = '#f6fbff';

const clamp = {
  extrapolateLeft: 'clamp' as const,
  extrapolateRight: 'clamp' as const,
};

const WaveBars: React.FC<{start: number; x: number; y: number}> = ({start, x, y}) => {
  const frame = useCurrentFrame();
  const bars = Array.from({length: 18});
  const reveal = interpolate(frame, [start, start + 30], [0, 1], clamp);

  return (
    <div
      style={{
        position: 'absolute',
        left: x,
        top: y,
        display: 'flex',
        alignItems: 'center',
        gap: 16,
        opacity: reveal,
        transform: `translateY(${interpolate(reveal, [0, 1], [26, 0])}px)`,
      }}
    >
      {bars.map((_, index) => {
        const pulse = Math.sin((frame - start) / 5 + index * 0.7);
        const height = 36 + Math.abs(pulse) * (index % 3 === 0 ? 130 : 88);
        return (
          <div
            key={index}
            style={{
              width: 12,
              height,
              borderRadius: 999,
              background:
                index % 4 === 0
                  ? lime
                  : `linear-gradient(180deg, ${teal}, ${violet})`,
              boxShadow: `0 0 30px rgba(71, 240, 223, ${0.18 + reveal * 0.32})`,
            }}
          />
        );
      })}
    </div>
  );
};

const Wordmark: React.FC<{scale?: number}> = ({scale = 1}) => {
  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 24 * scale,
        color: ink,
        fontFamily: 'Inter, Arial, sans-serif',
        fontSize: 96 * scale,
        fontWeight: 900,
        letterSpacing: 0,
        lineHeight: 1,
      }}
    >
      <div
        style={{
          width: 24 * scale,
          height: 116 * scale,
          borderRadius: 999,
          background: `linear-gradient(180deg, ${teal}, ${violet})`,
          boxShadow: '0 0 54px rgba(71, 240, 223, 0.72)',
        }}
      />
      <span>trnscrbr</span>
    </div>
  );
};

const TranscriptMorph: React.FC<{start: number}> = ({start}) => {
  const frame = useCurrentFrame();
  const inProgress = interpolate(frame, [start, start + 26], [0, 1], clamp);
  const afterIn = interpolate(frame, [start + 46, start + 84], [0, 1], clamp);

  return (
    <div
      style={{
        position: 'absolute',
        right: 140,
        bottom: 128,
        width: 680,
        border: '1px solid rgba(255,255,255,0.2)',
        borderRadius: 18,
        overflow: 'hidden',
        background: 'rgba(6, 15, 20, 0.88)',
        opacity: inProgress,
        transform: `translateY(${interpolate(inProgress, [0, 1], [46, 0])}px)`,
        boxShadow: '0 40px 100px rgba(0,0,0,0.34)',
      }}
    >
      <div
        style={{
          height: 62,
          borderBottom: '1px solid rgba(255,255,255,0.16)',
          display: 'flex',
          alignItems: 'center',
          gap: 12,
          paddingLeft: 24,
        }}
      >
        {[0, 1, 2].map((dot) => (
          <div
            key={dot}
            style={{
              width: 14,
              height: 14,
              borderRadius: 999,
              background: dot === 0 ? '#ff8d6b' : dot === 1 ? lime : teal,
            }}
          />
        ))}
      </div>
      <div
        style={{
          margin: 26,
          padding: 28,
          borderRadius: 14,
          color: '#8fa0aa',
          background: 'rgba(255,255,255,0.06)',
          fontFamily: 'Inter, Arial, sans-serif',
          fontSize: 32,
          lineHeight: 1.3,
        }}
      >
        um add the billing note and make it clearer
      </div>
      <div
        style={{
          margin: '0 26px 26px',
          padding: 30,
          borderRadius: 14,
          color: '#071018',
          background: `linear-gradient(135deg, ${teal}, #ffffff 58%, ${lime})`,
          fontFamily: 'Inter, Arial, sans-serif',
          fontSize: 34,
          fontWeight: 850,
          lineHeight: 1.25,
          opacity: afterIn,
          transform: `translateY(${interpolate(afterIn, [0, 1], [28, 0])}px)`,
        }}
      >
        Add the billing note and make the wording clearer.
      </div>
    </div>
  );
};

export const TrnscrbrIntro: React.FC<IntroProps> = ({domain}) => {
  const frame = useCurrentFrame();
  const {fps} = useVideoConfig();
  const hero = staticFile('/assets/trnscrbr-hero.png');
  const intro = spring({frame, fps, config: {damping: 18, stiffness: 80}});
  const titleIn = interpolate(frame, [34, 74], [0, 1], clamp);
  const CTAIn = interpolate(frame, [202, 236], [0, 1], clamp);
  const bgScale = interpolate(frame, [0, 270], [1.05, 1.16], clamp);
  const flash = interpolate(frame, [166, 180, 194], [0, 0.42, 0], clamp);

  return (
    <AbsoluteFill style={{backgroundColor: '#071016', overflow: 'hidden'}}>
      <Img
        src={hero}
        style={{
          position: 'absolute',
          inset: 0,
          width: '100%',
          height: '100%',
          objectFit: 'cover',
          transform: `scale(${bgScale}) translateX(${interpolate(frame, [0, 270], [-22, 10])}px)`,
          opacity: 0.72,
        }}
      />
      <AbsoluteFill
        style={{
          background:
            'linear-gradient(90deg, rgba(5,13,18,0.96) 0%, rgba(5,13,18,0.82) 36%, rgba(5,13,18,0.2) 100%)',
        }}
      />
      <AbsoluteFill style={{backgroundColor: `rgba(71, 240, 223, ${flash})`}} />

      <div
        style={{
          position: 'absolute',
          left: 128,
          top: 116,
          transform: `translateY(${interpolate(intro, [0, 1], [40, 0])}px)`,
          opacity: intro,
        }}
      >
        <Wordmark scale={0.56} />
      </div>

      <div
        style={{
          position: 'absolute',
          left: 136,
          top: 330,
          width: 880,
          fontFamily: 'Inter, Arial, sans-serif',
        }}
      >
        <div
          style={{
            color: teal,
            fontSize: 28,
            fontWeight: 900,
            letterSpacing: 2.2,
            textTransform: 'uppercase',
            opacity: titleIn,
            transform: `translateY(${interpolate(titleIn, [0, 1], [36, 0])}px)`,
          }}
        >
          {domain}
        </div>
        <div
          style={{
            marginTop: 26,
            color: ink,
            fontSize: 124,
            fontWeight: 920,
            letterSpacing: 0,
            lineHeight: 0.93,
            opacity: titleIn,
            transform: `translateY(${interpolate(titleIn, [0, 1], [48, 0])}px)`,
          }}
        >
          Speak once.
          <br />
          Paste polished.
        </div>
        <div
          style={{
            marginTop: 34,
            color: '#c4d3dc',
            fontSize: 38,
            lineHeight: 1.32,
            width: 760,
            opacity: interpolate(frame, [76, 112], [0, 1], clamp),
          }}
        >
          Push-to-talk AI dictation for the text field you are already using.
        </div>
      </div>

      <WaveBars start={92} x={138} y={804} />
      <TranscriptMorph start={128} />

      <div
        style={{
          position: 'absolute',
          left: 136,
          bottom: 98,
          display: 'flex',
          alignItems: 'center',
          gap: 24,
          opacity: CTAIn,
          transform: `translateY(${interpolate(CTAIn, [0, 1], [28, 0])}px)`,
          fontFamily: 'Inter, Arial, sans-serif',
        }}
      >
        <div
          style={{
            padding: '22px 30px',
            borderRadius: 12,
            background: ink,
            color: '#071018',
            fontSize: 30,
            fontWeight: 900,
          }}
        >
          Windows MVP
        </div>
        <div style={{color: ink, fontSize: 32, fontWeight: 800}}>
          Hold Ctrl + Win + Space
        </div>
      </div>
    </AbsoluteFill>
  );
};
