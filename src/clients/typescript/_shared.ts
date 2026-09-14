/**
 * The request handler function type.
 */
export type HttpRequestHandler = <T>(
    method: string,
    relativeUrl: string,
    acceptHeaderValue?: string,
    contentTypeValue?: string,
    content?: BodyInit,
    signal?: AbortSignal) => Promise<T>;

/**
 * A NexusException.
 */
export class NexusException extends Error {
    statusCode: string;

    constructor(statusCode: string, message: string) {
        super(message);
        this.name = "NexusException";
        this.statusCode = statusCode;
    }
}
