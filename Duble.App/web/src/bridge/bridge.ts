// bridge/bridge.ts — the channel to C#, typed by contract.ts.
//
//   request   {id, cmd, args}   response  {id, ok:true, result} | {id, ok:false, error:{code, message}}
//   event     {event, data}     (no id; C# -> here)
//
// WebView2 gives us window.chrome.webview. Outside it — a plain browser tab, a unit test — there is no host,
// and every call rejects with `no_host` rather than hanging forever.
import type { CommandArgs, CommandName, CommandResult, EventName, Events } from './contract';

/** The error codes C# sends. A view matches on these to show its own message instead of the raw text. */
export const ErrorCode = {
  UnknownCommand: 'unknown_command',
  BadArguments: 'bad_args',
  NoProject: 'no_project',
  Busy: 'busy',
  NotFound: 'not_found',
  Io: 'io',
  Cancelled: 'cancelled',
  Internal: 'internal',
  NoHost: 'no_host',
} as const;

export type ErrorCode = (typeof ErrorCode)[keyof typeof ErrorCode];

export class BridgeError extends Error {
  constructor(
    readonly code: ErrorCode | string,
    message: string,
  ) {
    super(message);
    this.name = 'BridgeError';
  }
}

interface Pending {
  resolve: (result: unknown) => void;
  reject: (error: BridgeError) => void;
}

const host = window.chrome?.webview;
const pending = new Map<string, Pending>();
const listeners = new Map<string, Set<(data: never) => void>>();
let nextId = 0;

host?.addEventListener('message', (event) => {
  const message = typeof event.data === 'string' ? (JSON.parse(event.data) as unknown) : (event.data as unknown);
  if (!message || typeof message !== 'object') return;

  const envelope = message as {
    id?: string;
    ok?: boolean;
    result?: unknown;
    error?: { code?: string; message?: string };
    event?: string;
    data?: unknown;
  };

  if (envelope.id !== undefined && pending.has(envelope.id)) {
    const waiting = pending.get(envelope.id)!;
    pending.delete(envelope.id);
    if (envelope.ok) waiting.resolve(envelope.result);
    else waiting.reject(new BridgeError(envelope.error?.code ?? ErrorCode.Internal, envelope.error?.message ?? 'unknown failure'));
    return;
  }

  if (envelope.event) {
    for (const listener of listeners.get(envelope.event) ?? []) {
      try {
        (listener as (data: unknown) => void)(envelope.data);
      } catch (failure) {
        console.error(`listener for ${envelope.event} threw`, failure);
      }
    }
  }
});

export const bridge = {
  /** Whether we are running inside the application at all. */
  get available(): boolean {
    return !!host;
  },

  /**
   * Runs a command and resolves with its result, or rejects with a BridgeError carrying the code.
   *
   * Commands that take arguments demand them; commands that take none refuse them. That is what the tuple in
   * the signature buys, and it is why `invoke` exists below for the one caller that cannot use it.
   */
  call<K extends CommandName>(
    command: K,
    ...[args]: CommandArgs<K> extends null ? [args?: null] : [args: CommandArgs<K>]
  ): Promise<CommandResult<K>> {
    return bridge.invoke(command, (args ?? null) as CommandArgs<K>);
  },

  /** The same thing with the arguments passed plainly, for generic code that forwards them (see useCommand). */
  invoke<K extends CommandName>(command: K, args: CommandArgs<K>): Promise<CommandResult<K>> {
    if (!host) return Promise.reject(new BridgeError(ErrorCode.NoHost, 'not running inside Duble'));

    const id = String(++nextId);
    return new Promise<CommandResult<K>>((resolve, reject) => {
      pending.set(id, { resolve: resolve as (result: unknown) => void, reject });
      host.postMessage({ id, cmd: command, args: args ?? null });
    });
  },

  /** Subscribes to an event; the returned function unsubscribes. */
  on<K extends EventName>(event: K, listener: (data: Events[K]) => void): () => void {
    let forEvent = listeners.get(event);
    if (!forEvent) listeners.set(event, (forEvent = new Set()));
    forEvent.add(listener as (data: never) => void);
    return () => {
      forEvent.delete(listener as (data: never) => void);
    };
  },
};

/** The message the user should see for a failure, given the codes this caller knows how to explain. */
export function errorCodeOf(failure: unknown): string | undefined {
  return failure instanceof BridgeError ? failure.code : undefined;
}

export function messageOf(failure: unknown): string {
  return failure instanceof Error ? failure.message : String(failure);
}
