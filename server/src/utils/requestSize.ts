/**
 * The Revit plugin reads each request in one socket read, so requests are kept to 4 KiB.
 * The size is measured on the same JSON-RPC envelope SocketClient.sendCommand sends;
 * its request id is 19 characters long.
 */
export const MAX_REQUEST_BYTES = 4096;

const REQUEST_ID_PLACEHOLDER = "x".repeat(19);

/** Returns an error message when the request would be larger than 4 KiB, otherwise null. */
export function requestSizeError(method: string, params: unknown): string | null {
  const bytes = Buffer.byteLength(
    JSON.stringify({ jsonrpc: "2.0", method, params, id: REQUEST_ID_PLACEHOLDER }),
    "utf8"
  );
  if (bytes <= MAX_REQUEST_BYTES) {
    return null;
  }
  return `The request is ${bytes} bytes, over the ${MAX_REQUEST_BYTES}-byte (4 KiB) limit. Nothing was sent to Revit; split it into smaller calls.`;
}
