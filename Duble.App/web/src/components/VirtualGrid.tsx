// components/VirtualGrid.tsx — a grid of fixed-height tiles that only draws the rows on screen.
//
// A catalog can hold thousands of garments, and thousands of <img> in the document make scrolling stutter. So
// the rows in view (plus a couple either side) are the only ones that exist; the body is given the full height
// so that the scrollbar still tells the truth.
import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from 'react';

/** Rows kept above and below the viewport, so a scroll does not reach empty space before React catches up. */
const OVERSCAN = 2;

export function VirtualGrid<T>({
  items,
  rowHeight,
  minColumnWidth,
  gap,
  renderItem,
  empty,
  /** Remembers where this grid was scrolled to, under this key, for the length of the session. */
  scrollKey,
}: {
  items: readonly T[];
  rowHeight: number;
  minColumnWidth: number;
  gap: number;
  renderItem: (item: T, index: number) => ReactNode;
  empty?: ReactNode;
  scrollKey?: string;
}) {
  const container = useRef<HTMLDivElement>(null);
  const [viewport, setViewport] = useState({ width: 0, height: 0, scrollTop: 0 });

  useEffect(() => {
    const node = container.current;
    if (!node) return;

    // scroll fires far more often than a frame can be drawn; one measurement per frame is enough
    let frame = 0;
    const measure = () => {
      frame = 0;
      setViewport({ width: node.clientWidth, height: node.clientHeight, scrollTop: node.scrollTop });
    };
    const schedule = () => {
      if (!frame) frame = requestAnimationFrame(measure);
    };

    measure();
    node.addEventListener('scroll', schedule, { passive: true });
    const observer = new ResizeObserver(schedule);
    observer.observe(node);

    return () => {
      node.removeEventListener('scroll', schedule);
      observer.disconnect();
      if (frame) cancelAnimationFrame(frame);
    };
  }, []);

  const restored = useRef(false);
  useLayoutEffect(() => {
    const node = container.current;
    if (!scrollKey || restored.current || !node || items.length === 0) return;
    restored.current = true;

    const saved = Number(sessionStorage.getItem(scrollKey) ?? 0);
    if (saved > 0) node.scrollTop = saved;
  }, [scrollKey, items.length]);

  useEffect(() => {
    const node = container.current;
    if (!scrollKey || !node) return;
    // on the way out, because leaving and coming back should not lose the reader's place
    return () => sessionStorage.setItem(scrollKey, String(node.scrollTop));
  }, [scrollKey]);

  // -2 leaves room for a border rounding the measurement the wrong way
  const columns = Math.max(1, Math.floor((viewport.width - 2 + gap) / (minColumnWidth + gap)));
  const stride = rowHeight + gap;
  const rows = Math.ceil(items.length / columns);

  const firstRow = Math.max(0, Math.floor(viewport.scrollTop / stride) - OVERSCAN);
  const lastRow = Math.min(rows - 1, Math.ceil((viewport.scrollTop + viewport.height) / stride) + OVERSCAN);

  const drawn = [];
  for (let row = firstRow; row <= lastRow; row++) {
    const cells = [];
    for (let column = 0; column < columns; column++) {
      const index = row * columns + column;
      if (index >= items.length) break;
      cells.push(renderItem(items[index]!, index));
    }
    drawn.push(
      <div
        key={row}
        className="vgrid-row"
        style={{ top: row * stride, height: rowHeight, gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))`, gap }}
      >
        {cells}
      </div>,
    );
  }

  return (
    <div ref={container} className="vgrid cat-grid">
      <div className="vgrid-body" style={{ height: items.length ? Math.max(0, rows * stride - gap) : undefined }}>
        {items.length === 0 ? empty : drawn}
      </div>
    </div>
  );
}
