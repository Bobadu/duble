// bridge/hooks.ts — reading from the host inside React.
//
// There is no HTTP cache to manage here: the data is local, fetching it costs a millisecond, and C# pushes an
// event whenever something changes. So instead of a query library this is one hook that runs a command, keeps
// the last answer, and runs again when the events a screen names arrive.
import { useCallback, useEffect, useRef, useState } from 'react';
import { bridge } from './bridge';
import type { CommandArgs, CommandName, CommandResult, EventName, Events } from './contract';

/**
 * Subscribes to a host event for as long as the component is mounted. The handler may be a fresh closure on
 * every render — it is read through a ref, so the subscription itself is made once.
 */
export function useBridgeEvent<K extends EventName>(event: K, handler: (data: Events[K]) => void): void {
  const latest = useRef(handler);
  latest.current = handler;

  useEffect(() => bridge.on(event, (data) => latest.current(data)), [event]);
}

export interface CommandState<T> {
  data: T | undefined;
  error: unknown;
  /** True while a call is in flight. A reload keeps the previous data on screen rather than blanking it. */
  loading: boolean;
  reload: () => void;
}

export interface CommandOptions {
  /** Skip the call entirely — for a screen with nothing to ask about yet, such as "no project is open". */
  enabled?: boolean;
  /** Host events that make the answer stale; each one runs the command again. */
  reloadOn?: readonly EventName[];
}

/**
 * Runs a command and keeps its result. Runs again when the arguments change, when one of `reloadOn` arrives,
 * or when `reload()` is called.
 *
 * The arguments are compared by their JSON rather than by identity: every render builds a new object literal,
 * and using that as a dependency would call the host forever.
 */
export function useCommand<K extends CommandName>(
  command: K,
  args: CommandArgs<K>,
  options: CommandOptions = {},
): CommandState<CommandResult<K>> {
  const { enabled = true, reloadOn } = options;

  const argumentsKey = JSON.stringify(args ?? null);
  const reloadKey = reloadOn?.join('|') ?? '';

  const latestArguments = useRef(args);
  latestArguments.current = args;

  const [state, setState] = useState<{ data?: CommandResult<K>; error?: unknown; loading: boolean }>({ loading: enabled });
  const [attempt, setAttempt] = useState(0);
  const reload = useCallback(() => setAttempt((previous) => previous + 1), []);

  useEffect(() => {
    if (!enabled) {
      setState({ loading: false });
      return;
    }

    let cancelled = false;
    setState((previous) => ({ ...previous, loading: true }));

    bridge
      .invoke(command, latestArguments.current)
      .then((data) => {
        if (!cancelled) setState({ data, loading: false });
      })
      .catch((error: unknown) => {
        if (!cancelled) setState((previous) => ({ data: previous.data, error, loading: false }));
      });

    // a superseded call may still be in flight; whatever it answers is now nobody's
    return () => {
      cancelled = true;
    };
  }, [command, argumentsKey, enabled, attempt]);

  useEffect(() => {
    if (!reloadOn?.length) return;
    const unsubscribe = reloadOn.map((event) => bridge.on(event, reload));
    return () => unsubscribe.forEach((off) => off());
  }, [reloadKey, reload]);

  return { data: state.data, error: state.error, loading: state.loading, reload };
}
