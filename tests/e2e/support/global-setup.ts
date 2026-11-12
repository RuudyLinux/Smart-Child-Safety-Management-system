import { startStack, stopStack } from './stack';

export default async function globalSetup() {
  stopStack(); // leftovers from an interrupted run
  await startStack();
}
