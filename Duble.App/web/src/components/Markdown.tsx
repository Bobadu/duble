// components/Markdown.tsx — the changelog and release notes, drawn.
//
// Not a Markdown engine: exactly the dialect CHANGELOG.md is written in — headings, bullet lists, paragraphs,
// **bold**, `code` and [links](…) — built as React elements, so text that arrived over the network is never
// injected as HTML. Anything the dialect does not know stays visible as plain text rather than vanishing.
import type { ReactNode } from 'react';
import { bridge } from '../bridge/bridge';

type Block =
  | { kind: 'heading'; level: number; text: string }
  | { kind: 'list'; items: string[] }
  | { kind: 'paragraph'; text: string };

/** One pass over the lines: headings and blanks split, wrapped lines join the bullet or paragraph above. */
function parse(markdown: string): Block[] {
  const blocks: Block[] = [];
  let list: string[] | null = null;
  let paragraph: string[] = [];

  const flush = () => {
    if (paragraph.length) blocks.push({ kind: 'paragraph', text: paragraph.join(' ') });
    if (list) blocks.push({ kind: 'list', items: list });
    paragraph = [];
    list = null;
  };

  for (const raw of markdown.split('\n')) {
    const line = raw.trimEnd();
    const heading = /^(#{1,3})\s+(.*)$/.exec(line);
    const bullet = /^-\s+(.*)$/.exec(line);

    if (!line.trim()) {
      flush();
    } else if (heading) {
      flush();
      blocks.push({ kind: 'heading', level: (heading[1] ?? '#').length, text: heading[2] ?? '' });
    } else if (/^\[[^\]]+\]:\s/.test(line)) {
      flush(); // a link-reference definition is an address for other lines, not a sentence
    } else if (bullet) {
      if (paragraph.length) flush();
      (list ??= []).push(bullet[1] ?? '');
    } else if (list && /^\s/.test(raw)) {
      list[list.length - 1] += ' ' + line.trim();
    } else {
      if (list) flush();
      paragraph.push(line.trim());
    }
  }
  flush();
  return blocks;
}

const INLINE = /\*\*([^*]+)\*\*|`([^`]+)`|\[([^\]]+)\]\((https?:\/\/[^)\s]+)\)/g;

function openExternally(url: string): void {
  void bridge.call('shell.openUrl', { url }).catch(() => undefined);
}

/** `**bold**`, `` `code` `` and [links](…), as elements. Bold may carry code or a link inside itself. */
function inline(text: string, withinBold = false): ReactNode[] {
  const parts: ReactNode[] = [];
  let last = 0;

  for (const match of text.matchAll(INLINE)) {
    const at = match.index ?? 0;
    if (at > last) parts.push(text.slice(last, at));

    const [whole, bold, code, label, url] = match;
    if (bold !== undefined) {
      // bold inside bold is not a thing; written anyway, it stays visible as the asterisks it is
      parts.push(withinBold ? whole : <b key={at}>{inline(bold, true)}</b>);
    } else if (code !== undefined) {
      parts.push(<code key={at}>{code}</code>);
    } else if (label !== undefined && url !== undefined) {
      parts.push(
        <a
          key={at}
          href={url}
          onClick={(event) => {
            event.preventDefault();
            openExternally(url);
          }}
        >
          {label}
        </a>,
      );
    }
    last = at + whole.length;
  }

  if (last < text.length) parts.push(text.slice(last));
  return parts;
}

export function Markdown({ text }: { text: string }) {
  return (
    <div className="md">
      {parse(text).map((block, at) => {
        switch (block.kind) {
          case 'heading': {
            // inside a card or a section that already has the page's h1/h2, so headings start two sizes down
            const Heading = (['h3', 'h4', 'h5'] as const)[block.level - 1] ?? 'h5';
            return <Heading key={at}>{inline(block.text)}</Heading>;
          }
          case 'list':
            return (
              <ul key={at}>
                {block.items.map((item, index) => (
                  <li key={index}>{inline(item)}</li>
                ))}
              </ul>
            );
          case 'paragraph':
            return <p key={at}>{inline(block.text)}</p>;
        }
      })}
    </div>
  );
}
