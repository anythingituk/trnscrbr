import React from 'react';
import {Composition} from 'remotion';
import {TrnscrbrIntro} from './trnscrbr-intro';

export const Root: React.FC = () => {
  return (
    <Composition
      id="TrnscrbrIntro"
      component={TrnscrbrIntro}
      durationInFrames={270}
      fps={30}
      width={1920}
      height={1080}
      defaultProps={{
        domain: 'trnscrbr.ai',
      }}
    />
  );
};
