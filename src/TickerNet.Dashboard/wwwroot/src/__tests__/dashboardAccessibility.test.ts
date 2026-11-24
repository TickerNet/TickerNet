import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const srcRoot = join(dirname(fileURLToPath(import.meta.url)), '..');

function vueFiles(dir: string): string[] {
    return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
        const fullPath = join(dir, entry.name);
        if (entry.isDirectory()) return vueFiles(fullPath);
        return entry.name.endsWith('.vue') ? [fullPath] : [];
    });
}

function visibleButtonText(rawInner: string): string {
    return rawInner
        .replace(/<!--[\s\S]*?-->/g, '')
        .replace(/<v-tooltip\b[^>]*>[\s\S]*?<\/v-tooltip>/g, '')
        .replace(/<v-icon\b[\s\S]*?<\/v-icon>/g, '')
        .replace(/<[^>]+>/g, ' ')
        .replace(/\s+/g, ' ')
        .trim();
}

function unlabeledIconButtons(): string[] {
    const buttonPattern = /<v-btn\b([\s\S]*?)(?:\/>|>([\s\S]*?)<\/v-btn>)/g;
    const missing: string[] = [];

    for (const file of vueFiles(srcRoot)) {
        const source = readFileSync(file, 'utf8');
        let match: RegExpExecArray | null;
        buttonPattern.lastIndex = 0;

        while ((match = buttonPattern.exec(source)) !== null) {
            const attrs = match[1].replace(/\s+/g, ' ').trim();
            const rawInner = match[2] ?? '';
            const hasIcon = /(?:^|\s):?icon(?:=|\s|$)/.test(attrs) || /<v-icon\b/.test(rawInner);
            const hasTextProp = /(?:^|\s):?text(?:=|\s|$)/.test(attrs);
            const hasLabel = /\baria-label\b/.test(attrs);

            if (hasIcon && !hasTextProp && !hasLabel && !visibleButtonText(rawInner)) {
                const line = source.slice(0, match.index).split('\n').length;
                missing.push(`${relative(srcRoot, file).replace(/\\/g, '/')}:${line}`);
            }
        }
    }

    return missing;
}

describe('dashboard accessibility', () => {
    it('gives every icon-only button an accessible name', () => {
        expect(unlabeledIconButtons().join('\n')).toBe('');
    });

    it('makes the header branding a real link', () => {
        const layout = readFileSync(
            join(srcRoot, 'components', 'layout', 'DashboardLayout.vue'),
            'utf8',
        );

        expect(layout).toMatch(/<router-link[^>]*to="\/"[^>]*aria-label="TickerNet dashboard home"/);
        expect(layout).not.toMatch(/@click="navigateToDashboard"/);
    });
});
