import { test } from 'node:test'
import assert from 'node:assert/strict'
import { withFeature } from './featureGrants.ts'

test('turning a feature on adds it and keeps the others, sorted', () => {
  assert.deepEqual(withFeature(['crew', 'events'], 'groups', true), ['crew', 'events', 'groups'])
  assert.deepEqual(withFeature(['events'], 'crew', true), ['crew', 'events'])
})

test('turning a feature off removes only that one', () => {
  assert.deepEqual(withFeature(['crew', 'groups', 'events'], 'groups', false), ['crew', 'events'])
})

test('features the page does not know are kept', () => {
  assert.deepEqual(withFeature(['future-thing'], 'groups', true), ['future-thing', 'groups'])
})

test('on twice or off when absent changes nothing', () => {
  assert.deepEqual(withFeature(['groups'], 'groups', true), ['groups'])
  assert.deepEqual(withFeature([], 'groups', false), [])
})
