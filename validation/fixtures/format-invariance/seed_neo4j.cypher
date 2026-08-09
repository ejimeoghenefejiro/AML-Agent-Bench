// Seeds the same relationship graph as relationships.graphml in this directory,
// for FormatInvarianceTests's live Neo4j comparison (gated on
// AML_CONN_TEST_FORMAT_INVARIANCE_NEO4J being set).
MATCH (n) DETACH DELETE n;

CREATE (:Account {id: 'ACC-ALPHA', name: 'Alpha'})
CREATE (:Account {id: 'ACC-BETA',  name: 'Beta'})
CREATE (:Account {id: 'ACC-GAMMA', name: 'Gamma'})
CREATE (:Account {id: 'ACC-DELTA', name: 'Delta'});

MATCH (a:Account {id: 'ACC-ALPHA'}), (b:Account {id: 'ACC-BETA'})
CREATE (a)-[:TRANSFERRED_TO {id: 'REL-001', evidence_ids: ['INV-001']}]->(b);

MATCH (a:Account {id: 'ACC-BETA'}), (b:Account {id: 'ACC-GAMMA'})
CREATE (a)-[:TRANSFERRED_TO {id: 'REL-002', evidence_ids: ['INV-002']}]->(b);

MATCH (a:Account {id: 'ACC-GAMMA'}), (b:Account {id: 'ACC-DELTA'})
CREATE (a)-[:TRANSFERRED_TO {id: 'REL-003', evidence_ids: ['INV-003']}]->(b);
