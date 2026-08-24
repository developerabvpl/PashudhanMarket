import nx from '@nx/eslint-plugin';

export default [
  ...nx.configs['flat/base'],
  ...nx.configs['flat/typescript'],
  ...nx.configs['flat/javascript'],
  {
    ignores: [
      '**/dist',
      '**/vite.config.*.timestamp*',
      '**/vitest.config.*.timestamp*',
    ],
  },
  {
    files: ['**/*.ts', '**/*.tsx', '**/*.js', '**/*.jsx'],
    rules: {
      '@nx/enforce-module-boundaries': [
        'error',
        {
          enforceBuildableLibDependency: true,
          allow: ['^.*/eslint(\\.base)?\\.config\\.[cm]?[jt]s$'],
          // Dependencies run one way only: util <- ui <- auth <- data-access <- apps.
          // This is the web mirror of the API's module rules; a library may never reach
          // sideways or upwards.
          depConstraints: [
            {
              sourceTag: 'type:app',
              onlyDependOnLibsWithTags: [
                'type:data-access',
                'type:auth',
                'type:ui',
                'type:util',
              ],
            },
            {
              sourceTag: 'type:data-access',
              onlyDependOnLibsWithTags: ['type:auth', 'type:ui', 'type:util'],
            },
            { sourceTag: 'type:auth', onlyDependOnLibsWithTags: ['type:ui', 'type:util'] },
            { sourceTag: 'type:ui', onlyDependOnLibsWithTags: ['type:util'] },
            { sourceTag: 'type:util', onlyDependOnLibsWithTags: [] },
            { sourceTag: 'type:e2e', onlyDependOnLibsWithTags: [] },
          
          ],
        },
      ],
    },
  },
  {
    files: [
      '**/*.ts',
      '**/*.tsx',
      '**/*.cts',
      '**/*.mts',
      '**/*.js',
      '**/*.jsx',
      '**/*.cjs',
      '**/*.mjs',
    ],
    // Override or add rules here
    rules: {},
  },
  {
    files: ['**/*.spec.ts', '**/*.spec.mts'],
    rules: {
      // Tests deliberately poke at shapes and reach into the DOM; the strictness that
      // protects application code just adds ceremony here.
      '@typescript-eslint/no-explicit-any': 'off',
      '@typescript-eslint/no-non-null-assertion': 'off',
    },
  },
];