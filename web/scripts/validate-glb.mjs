// Validates the generated GLB with the Khronos glTF-Validator and checks that every
// component node listed in data/components.json exists in the model.
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import validator from 'gltf-validator';

const here = path.dirname(fileURLToPath(import.meta.url));
const glbPath = process.argv[2] ?? path.join(here, '../public/models/VCR_System.glb');
const data = JSON.parse(readFileSync(path.join(here, '../../data/components.json'), 'utf8'));
const bytes = new Uint8Array(readFileSync(glbPath));

const report = await validator.validateBytes(bytes);
const { numErrors, numWarnings, numInfos } = report.issues;
console.log(`glTF-Validator: ${numErrors} errors, ${numWarnings} warnings, ${numInfos} infos`);
for (const m of report.issues.messages.filter((m) => m.severity <= 1).slice(0, 20)) console.log(' ', m.code, m.message, m.pointer ?? '');

// parse JSON chunk to check node names
const dv = new DataView(bytes.buffer);
const jsonLen = dv.getUint32(12, true);
const gltf = JSON.parse(new TextDecoder().decode(bytes.subarray(20, 20 + jsonLen)));
const names = new Set(gltf.nodes.map((n) => n.name));
const missing = data.components.filter((c) => !names.has(c.node)).map((c) => c.node);
console.log(`nodes: ${gltf.nodes.length}, meshes: ${gltf.meshes.length}, materials: ${gltf.materials.length}`);
console.log(missing.length ? `MISSING component nodes: ${missing.join(', ')}` : `All ${data.components.length} component nodes present.`);
process.exit(numErrors || missing.length ? 1 : 0);
