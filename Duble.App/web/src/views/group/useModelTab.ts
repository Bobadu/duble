// views/group/useModelTab.ts — which tab a card was left on, remembered for the session.
//
// It lives apart from ModelTab so that a card can read it without pulling three.js in: the 3D tab is loaded
// only when it is actually opened.
import { useState } from 'react';

export type CardTab = '2d' | '3d';

export function useModelTab(key: string): [CardTab, (tab: CardTab) => void] {
  const [tab, setTab] = useState<CardTab>(() => (sessionStorage.getItem(key) === '3d' ? '3d' : '2d'));

  return [
    tab,
    (next) => {
      sessionStorage.setItem(key, next);
      setTab(next);
    },
  ];
}
