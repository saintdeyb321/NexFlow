export const compareTimestamps = (left: string, right: string): number => {
  const milliseconds = Date.parse(left) - Date.parse(right);
  if (milliseconds) return milliseconds;
  const fraction = (value: string) => Number((value.match(/\.(\d+)/)?.[1] ?? '').padEnd(9, '0'));
  return fraction(left) - fraction(right);
};

export const mergeById = <T extends { id: string }>(current: readonly T[], delta: readonly T[], compare: (a: T, b: T) => number): T[] => {
  const records = new Map(current.map(record => [record.id, record]));
  for (const record of delta) records.set(record.id, record);
  return [...records.values()].sort(compare);
};

// Inclusive time cursors deduplicate across polls; document IDs paginate a full timestamp bucket without skipping peers.
export const fetchIncremental = async <T extends { id: string }>(
  load: (after?: string, afterId?: string) => Promise<T[]>,
  timestamp: (record: T) => string,
  after?: string,
  limit = 50,
): Promise<T[]> => {
  if (!after) return load();
  const result: T[] = [];
  let cursor = after;
  let afterId: string | undefined;
  while (true) {
    const page = await load(cursor, afterId);
    result.push(...page);
    if (page.length < limit) return result;
    const last = page[page.length - 1];
    const next = timestamp(last);
    if (cursor === next && afterId === last.id) throw new Error('El cursor incremental no avanzó.');
    cursor = next;
    afterId = last.id;
  }
};
