/**
 * Result pattern — mismo shape que Result<T> del backend .NET.
 * Los fallos esperados (validaciones, reglas de dominio) NO lanzan excepciones;
 * las excepciones se reservan para bugs y fallas de infraestructura.
 */

export interface DomainError {
  readonly code: string;
  readonly message: string;
}

export function makeError(code: string, message: string): DomainError {
  return { code, message };
}

export type Result<T = void> =
  | { readonly isSuccess: true; readonly value: T }
  | { readonly isSuccess: false; readonly error: DomainError };

export const Result = {
  ok<T>(value: T): Result<T> {
    return { isSuccess: true, value };
  },
  okVoid(): Result<void> {
    return { isSuccess: true, value: undefined };
  },
  fail<T = never>(error: DomainError): Result<T> {
    return { isSuccess: false, error };
  },
};

export function isFailure<T>(result: Result<T>): result is { isSuccess: false; error: DomainError } {
  return !result.isSuccess;
}

export function isSuccess<T>(result: Result<T>): result is { isSuccess: true; value: T } {
  return result.isSuccess;
}
